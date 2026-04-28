using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using DevOpsMigrationPlatform.CLI.Views;
using DevOpsMigrationPlatform.Infrastructure.Config;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Abstractions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Submits a migration job to the control plane. Behaviour (export, import, or full
/// lifecycle) is determined by the <c>mode</c> field in the configuration file.
///
/// All source types (AzureDevOpsServices, TeamFoundationServer, Simulated) submit
/// jobs to the control plane. The appropriate agent picks up the job via
/// capability-based routing.
///
/// No migration logic runs in this command — all execution happens in the agent.
/// See docs/cli.md and system-architecture guardrail rules 16 and 19.
/// </summary>
public sealed class QueueCommand : ControlPlaneCommandBase<QueueCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        QueueCommandSettings settings,
        CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddSingleton<IConfigurationService, ConfigurationService>();

            services.AddHttpClient<ControlPlaneClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<EnvironmentOptions>>().Value;
                client.BaseAddress = new Uri(opts.ControlPlane.BaseUrl);
            });

            services.AddTransient<IJobSubmissionClient>(sp => sp.GetRequiredService<ControlPlaneClient>());

            services.AddSingleton<IProgressSink, AnsiProgressSink>();
        });

        var config = await LoadConfigurationAsync(settings, cancellationToken);
        if (config is null)
            return 1;

        // Route to the appropriate handler based on config mode.
        return config.Mode switch
        {
            "Export" => await ExecuteExportAsync(config, settings, cancellationToken),
            "Import" => await ExecuteImportAsync(config, settings, cancellationToken),
            "Both" => await ExecuteBothAsync(config, settings, cancellationToken),
            _ => ExecuteInvalidMode(config.Mode)
        };
    }

    private async Task<int> ExecuteImportAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();

        var orgUrl = config.Target?.GetResolvedUrl();
        var project = config.Target?.GetProject();
        var packagePath = config.Package?.ExpandedPath;

        if (string.IsNullOrWhiteSpace(orgUrl))
        {
            ShowError(console, "Target.Url is required for import. Set it in the config file.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            ShowError(console, "Target.Project is required for import. Set it in the config file.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(packagePath))
        {
            ShowError(console, "Package.WorkingDirectory is required for import. Set it in the config file.");
            return 1;
        }

        var outputPath = Path.Combine(
            Path.GetFullPath(packagePath),
            CliPathUtilities.ExtractOrgFolderName(orgUrl),
            project);
        console.MarkupLine($"[blue]ℹ[/] Importing into [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path   : [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);

        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            Mode = "Import",
            Target = config.Target,
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Package!.CreatePackage
            },
            Modules = modules,
            Diagnostics = new JobDiagnostics { MinimumLevel = settings.Level },
            Resume = settings.ForceFresh ? new JobResume { Mode = ResumeMode.ForceFresh } : null
        };

        var envOpts = GetRequiredService<IOptions<EnvironmentOptions>>().Value;
        var isStandaloneMode = envOpts.Type == EnvironmentType.Standalone;
        var shouldFollow = settings.Follow || isStandaloneMode;

        var client = GetRequiredService<ControlPlaneClient>();

        if (!shouldFollow)
        {
            var jobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, jobId, GetControlPlaneUrl());
            console.MarkupLine($"[grey]Use [blue]manage status --job {jobId}[/] to check progress.[/]");
            return 0;
        }

        Guid parsedJobId;
        try
        {
            parsedJobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, parsedJobId, GetControlPlaneUrl());
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to submit job: {ex.Message}");
            return 1;
        }

        ProgressEvent? lastEvt = null;
        var jobFailed = false;
        using var followCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var followCtsDisposed = false;
        ConsoleCancelEventHandler ctrlCHandler = (_, e) =>
        {
            e.Cancel = true;
            if (!followCtsDisposed)
                followCts.Cancel();
        };
        Console.CancelKeyPress += ctrlCHandler;

        try
        {
            await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
            {
                lastEvt = evt;
                console.MarkupLine($"[grey]{Markup.Escape(evt.Stage ?? string.Empty)}[/] {Markup.Escape(evt.Message ?? string.Empty)}");
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
        {
            jobFailed = true;
            ShowError(console, ex.Message);
        }
        catch (OperationCanceledException)
        {
            console.MarkupLine("[yellow]Detached from stream. Job continues running.[/]");
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= ctrlCHandler;
            followCtsDisposed = true;
        }

        await followCts.CancelAsync();
        if (jobFailed) return 1;

        if (lastEvt is not null)
        {
            var importedCount = lastEvt.Metrics?.Migration?.WorkItems?.Completed ?? 0;
            ShowSuccess(console, $"Import complete — {importedCount} work items imported.");
        }
        else
            ShowSuccess(console, "Work item import complete.");
        return 0;
    }

    private int ExecuteImportStub()
    {
        AnsiConsole.MarkupLine("[grey]import — not available in this release.[/]");
        return 1;
    }

    private async Task<int> ExecuteBothAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var exportResult = await ExecuteExportAsync(config, settings, cancellationToken);
        if (exportResult != 0)
            return exportResult;

        return await ExecuteImportAsync(config, settings, cancellationToken);
    }

    private int ExecuteInvalidMode(string mode)
    {
        AnsiConsole.MarkupLine($"[red]Invalid mode '{Markup.Escape(mode)}'. Must be Export, Import, or Both.[/]");
        return 1;
    }

    private async Task<int> ExecuteExportAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        // Simulated source: no WIQL, no discovery, no credentials — build a minimal job.
        if (string.Equals(config.Source?.Type, "Simulated", StringComparison.Ordinal))
            return await ExecuteSimulatedExportAsync(config, settings, cancellationToken);

        // All other source types (AzureDevOpsServices, TeamFoundationServer) submit
        // to the control plane. The appropriate agent (MigrationAgent or TfsMigrationAgent)
        // picks up the job via capability routing.
        return await ExecuteAdoExportAsync(config, settings, cancellationToken);
    }

    private async Task<int> ExecuteSimulatedExportAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();

        var orgUrl = config.Source?.GetResolvedUrl() ?? "https://simulated.example.com";
        var project = config.Source?.GetProject() ?? "SimulatedProject";

        var outputPath = Path.Combine(
            Path.GetFullPath(config.Package.ExpandedPath),
            CliPathUtilities.ExtractOrgFolderName(orgUrl),
            project);

        console.MarkupLine($"[blue]ℹ[/] Exporting from [bold]Simulated[/] source");
        console.MarkupLine($"[blue]ℹ[/] Package path  : [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);

        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            Mode = "Export",
            Source = config.Source,
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Package.CreatePackage
            },
            Modules = modules,
            Diagnostics = new JobDiagnostics { MinimumLevel = settings.Level },
            Resume = settings.ForceFresh ? new JobResume { Mode = ResumeMode.ForceFresh } : null
        };

        var envOpts = GetRequiredService<IOptions<EnvironmentOptions>>().Value;
        var isStandaloneMode = envOpts.Type == EnvironmentType.Standalone;
        var shouldFollow = settings.Follow || isStandaloneMode;

        var client = GetRequiredService<ControlPlaneClient>();

        if (!shouldFollow)
        {
            var jobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, jobId, GetControlPlaneUrl());
            console.MarkupLine($"[grey]Use [blue]manage status --job {jobId}[/] to check progress.[/]");
            return 0;
        }

        Guid parsedJobId;
        try
        {
            parsedJobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, parsedJobId, GetControlPlaneUrl());
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to submit job: {ex.Message}");
            return 1;
        }

        ProgressEvent? lastEvt = null;
        var jobFailed = false;
        using var followCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var followCtsDisposed = false;
        ConsoleCancelEventHandler ctrlCHandler = (_, e) =>
        {
            e.Cancel = true;
            if (!followCtsDisposed)
                followCts.Cancel();
        };
        Console.CancelKeyPress += ctrlCHandler;

        try
        {
            await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
            {
                lastEvt = evt;
                console.MarkupLine($"[grey]{Markup.Escape(evt.Stage ?? string.Empty)}[/] {Markup.Escape(evt.Message ?? string.Empty)}");
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
        {
            jobFailed = true;
            ShowError(console, ex.Message);
        }
        catch (OperationCanceledException)
        {
            console.MarkupLine("[yellow]Detached from stream. Job continues running.[/]");
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= ctrlCHandler;
            followCtsDisposed = true;
        }

        await followCts.CancelAsync();
        if (jobFailed) return 1;

        if (lastEvt is not null)
        {
            var wiCount = lastEvt.Metrics?.Migration?.WorkItems?.Completed ?? 0;
            var revCount = lastEvt.Metrics?.Migration?.WorkItems?.RevisionsProcessed ?? 0;
            ShowSuccess(console, $"Export complete — {wiCount} work items / {revCount} revisions written to package.");
        }
        else
            ShowSuccess(console, "Simulated export complete.");
        return 0;
    }

    private async Task<int> ExecuteAdoExportAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();

        var orgUrl = config.Source?.GetResolvedUrl();
        var project = config.Source?.GetProject();

        if (string.IsNullOrWhiteSpace(orgUrl))
        {
            ShowError(console, "Source.Url is required. Set it in the config file.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            ShowError(console, "Source.Project is required. Set it in the config file.");
            return 1;
        }

        var outputPath = Path.Combine(
            Path.GetFullPath(config.Package.ExpandedPath),
            CliPathUtilities.ExtractOrgFolderName(orgUrl),
            project);

        console.MarkupLine($"[blue]ℹ[/] Exporting from [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path  : [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);

        // Pre-flight: count work items so we can show a deterministic progress bar.
        var pat = (config.Source as AzureDevOpsEndpointOptions)?.Authentication?.ResolvedAccessToken ?? string.Empty;
        var baseQuery = config.Modules.WorkItems.Enabled
            ? config.Modules.WorkItems.Scope.Query
            : null;

        // Validate WIQL query for safety and correctness before execution
        var validationResult = WiqlValidator.Validate(baseQuery);
        if (!validationResult.IsValid)
        {
            ShowError(console, $"Invalid WIQL query: {validationResult.ErrorMessage}");
            return 1;
        }

        var totalWorkItems = 0;

        // Build MigrationJob — no migration logic here.
        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            Mode = "Export",
            Source = config.Source,
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Package.CreatePackage
            },
            Modules = modules,
            Diagnostics = new JobDiagnostics { MinimumLevel = settings.Level },
            Resume = settings.ForceFresh ? new JobResume { Mode = ResumeMode.ForceFresh } : null
        };

        // Determine follow mode: explicit --follow, or implicit in standalone mode.
        var envOpts = GetRequiredService<IOptions<EnvironmentOptions>>().Value;
        var isStandaloneMode = envOpts.Type == EnvironmentType.Standalone;
        var shouldFollow = settings.Follow || isStandaloneMode;

        var client = GetRequiredService<ControlPlaneClient>();

        // Non-follow remote mode: submit and exit immediately (FR-025).
        if (!shouldFollow)
        {
            var jobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, jobId, GetControlPlaneUrl());
            console.MarkupLine($"[grey]Use [blue]manage status --job {jobId}[/] to check progress.[/]");
            return 0;
        }

        // Follow mode: stream progress + diagnostics concurrently.
        var parsedJobId = Guid.Parse(job.JobId);
        ProgressEvent? lastEvt = null;
        var jobFailed = false;

        // Use a linked CTS that we cancel on Ctrl+C to detach from diagnostics
        // without cancelling the job.
        using var followCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Guard against the handler firing after followCts has been disposed (e.g. a
        // second Ctrl+C after the first already triggered detach and the using block exited).
        var followCtsDisposed = false;
        ConsoleCancelEventHandler ctrlCHandler = (_, e) =>
        {
            e.Cancel = true; // Prevent process exit.
            if (!followCtsDisposed)
                followCts.Cancel();
        };
        Console.CancelKeyPress += ctrlCHandler;

        // Submit the job first.
        try
        {
            parsedJobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, parsedJobId, GetControlPlaneUrl());
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to submit job: {ex.Message}");
            return 1;
        }

        // Diagnostics records are buffered while the Progress() renderer owns the
        // console, then flushed afterwards.  Writing directly to the console while
        // Progress() is active causes rendering artefacts because Spectre's progress
        // renderer is not thread-safe with concurrent console writers.
        var diagnosticsBuffer = new System.Collections.Concurrent.ConcurrentQueue<string>();

        // Start diagnostics streaming as a background task — enqueue, don't print.
        var diagnosticsTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var record in client.StreamDiagnosticsAsync(parsedJobId, settings.Level, followCts.Token))
                {
                    var levelColor = record.Level switch
                    {
                        "Error" or "Critical" => "red",
                        "Warning" => "yellow",
                        "Debug" or "Trace" => "grey",
                        _ => "blue"
                    };
                    diagnosticsBuffer.Enqueue(
                        $"[{levelColor}]{Markup.Escape(record.Level)}[/] [[{Markup.Escape(record.Category)}]] {Markup.Escape(record.Message)}");
                }
            }
            catch (OperationCanceledException)
            {
                // Detach — user cancelled follow.
            }
            catch (Exception)
            {
                // Best-effort diagnostics streaming — don't propagate.
            }
        }, followCts.Token);

        var progressStartTime = DateTimeOffset.UtcNow;
        int completed = 0;
        int skipped = 0;
        int revisions = 0;
        int lastWiRevisions = 0;
        int currentWiId = 0;
        int currentWiIndex = 0;
        int currentWiRevsWritten = 0;
        double lastRevDurationMs = 0;
        double avgRevDurationMs = 0;
        string currentStage = string.Empty;
        int attProcessed = 0;
        int attFailed = 0;
        double avgAttDurationMs = 0;
        long avgAttSizeBytes = 0;
        string? currentAttName = null;
        string? lastWiStatus = null;

        // Channel 2: latest aggregate metrics polled from GET /jobs/{id}/telemetry every 5s.
        // All counter display reads from latestMetrics — never from evt.Metrics (Channel 1,
        // which is always null for .NET 10 agents).
        JobMetrics? latestMetrics = null;
        var telemetryTask = Task.Run(async () =>
        {
            try
            {
                while (!followCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var m = await client.GetTelemetryAsync(parsedJobId, followCts.Token).ConfigureAwait(false);
                        if (m is not null)
                            latestMetrics = m;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception) { /* best-effort — do not propagate */ }
                    await Task.Delay(5_000, followCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }, followCts.Token);

        if (Console.IsOutputRedirected)
        {
            // Non-interactive (redirected stdout — subprocess, CI, test runner): skip the
            // Live renderer entirely.  Cursor-positioning ANSI sequences throw "The handle
            // is invalid" on non-console handles; plain event iteration is sufficient.
            try
            {
                await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
                {
                    lastEvt = evt;
                    if (!string.IsNullOrEmpty(evt.Stage))
                        currentStage = evt.Stage;
                    // Read counters from Channel 2 (latestMetrics), not Channel 1 (evt.Metrics).
                    // evt.Metrics is always null for .NET 10 agents — only TFS subprocess populates it.
                    var wi = latestMetrics?.Migration?.WorkItems;
                    if (wi != null)
                    {
                        completed = (int)wi.Completed;
                        skipped = (int)wi.Skipped;
                        revisions = (int)(wi.RevisionsProcessed > 0 ? wi.RevisionsProcessed : revisions);
                        if (wi.LastRevisionDurationMs > 0)
                            lastRevDurationMs = wi.LastRevisionDurationMs;
                        if (wi.AverageRevisionDurationMs > 0)
                            avgRevDurationMs = wi.AverageRevisionDurationMs;
                        if (wi.LastWorkItemStatus != null)
                            lastWiStatus = wi.LastWorkItemStatus;
                    }
                    var att = latestMetrics?.Migration?.WorkItems?.Attachments;
                    if (att != null)
                    {
                        attProcessed = (int)att.Processed;
                        attFailed = (int)att.Failed;
                        avgAttDurationMs = att.AverageDownloadDurationMs;
                        avgAttSizeBytes = att.AverageSizeBytes;
                        currentAttName = att.CurrentAttachmentName;
                    }
                    if (wi?.CurrentWorkItemId > 0)
                    {
                        currentWiId = wi.CurrentWorkItemId;
                        currentWiIndex = wi.CurrentWorkItemIndex;
                        currentWiRevsWritten = wi.CurrentWorkItemRevisionsWritten;
                    }
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
            {
                jobFailed = true;
                ShowError(console, ex.Message);
                if (lastEvt is not null)
                    ShowError(console, $"Last progress: {completed} exported / {skipped} skipped / {revisions} revisions");
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }
        else
        {
            // Interactive path: redirect Console.Out to prevent ILogger output from
            // interleaving with the Live renderer, then render the progress bar.
            //
            // AnsiConsole.Console captured its TextWriter at creation time, so it always
            // writes to the real terminal regardless of Console.SetOut().  The .NET ILogger
            // ConsoleLogger calls Console.Out dynamically on each write — so redirecting
            // Console.Out to a buffer prevents any logger output from interleaving with the
            // Live renderer.  The buffer is flushed after Live() exits.
            var logBuffer = new StringWriter();
            var originalOut = Console.Out;
            var cancelled = false;
            Console.SetOut(logBuffer);

            try
            {
                await console.Live(BuildProgressRenderable(
                        0, 0, totalWorkItems, 0, 0, 0, 0, string.Empty))
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .StartAsync(async ctx =>
                    {
                        // Spectre.Console Live does NOT render until the callback
                        // calls Refresh/UpdateTarget. Force the initial render.
                        ctx.Refresh();

                        await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
                        {
                            lastEvt = evt;
                            if (!string.IsNullOrEmpty(evt.Stage))
                                currentStage = evt.Stage;
                            // Read counters from Channel 2 (latestMetrics), not Channel 1 (evt.Metrics).
                            // evt.Metrics is always null for .NET 10 agents — only TFS subprocess populates it.
                            var wi = latestMetrics?.Migration?.WorkItems;
                            if (wi != null)
                            {
                                completed = (int)wi.Completed;
                                skipped = (int)wi.Skipped;
                                revisions = (int)(wi.RevisionsProcessed > 0 ? wi.RevisionsProcessed : revisions);
                                if (wi.LastWorkItemRevisions > 0)
                                    lastWiRevisions = (int)wi.LastWorkItemRevisions;
                                if (wi.LastRevisionDurationMs > 0)
                                    lastRevDurationMs = wi.LastRevisionDurationMs;
                                if (wi.AverageRevisionDurationMs > 0)
                                    avgRevDurationMs = wi.AverageRevisionDurationMs;
                                if (wi.LastWorkItemStatus != null)
                                    lastWiStatus = wi.LastWorkItemStatus;
                                if (wi.CurrentWorkItemId > 0)
                                {
                                    currentWiId = wi.CurrentWorkItemId;
                                    currentWiIndex = wi.CurrentWorkItemIndex;
                                    currentWiRevsWritten = wi.CurrentWorkItemRevisionsWritten;
                                }
                            }

                            var att = latestMetrics?.Migration?.WorkItems?.Attachments;
                            if (att != null)
                            {
                                attProcessed = (int)att.Processed;
                                attFailed = (int)att.Failed;
                                avgAttDurationMs = att.AverageDownloadDurationMs;
                                avgAttSizeBytes = att.AverageSizeBytes;
                                currentAttName = att.CurrentAttachmentName;
                            }

                            ctx.UpdateTarget(BuildProgressRenderable(
                                completed, skipped, totalWorkItems,
                                currentWiId, currentWiRevsWritten,
                                currentWiIndex, lastWiRevisions,
                                currentStage, lastWiStatus,
                                evt.LastCheckpointAt, evt.NextCheckpointDueAt,
                                lastRevDurationMs, avgRevDurationMs,
                                revisions,
                                attProcessed, attFailed, avgAttDurationMs, avgAttSizeBytes, currentAttName,
                                latestMetrics?.Migration?.Teams,
                                latestMetrics?.Migration?.Nodes,
                                latestMetrics?.Migration?.Identities));
                        }
                    });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
            {
                jobFailed = true;
                ShowError(console, ex.Message);
                if (lastEvt is not null)
                    ShowError(console, $"Last progress: {completed} exported / {skipped} skipped / {revisions} revisions");
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                // Ctrl+C pressed — detach.
                if (isStandaloneMode)
                {
                    console.MarkupLine("[yellow]Cancelled. Job has been stopped.[/]");
                }
                else
                {
                    console.MarkupLine("[yellow]Detached from diagnostic stream. Job continues running on the server.[/]");
                    console.MarkupLine("[grey]Use [bold]tui[/] to resume watching.[/]");
                }
                return 0;
            }
            finally
            {
                // Deregister Ctrl+C handler and mark CTS as disposed before the using block
                // exits so a late-firing second Ctrl+C cannot call Cancel() on a disposed CTS.
                Console.CancelKeyPress -= ctrlCHandler;
                followCtsDisposed = true;

                // Restore stdout. On cancellation, discard the buffer — flushing thousands
                // of accumulated Information-level log lines would flood the terminal.
                Console.SetOut(originalOut);
                if (!cancelled)
                {
                    var captured = logBuffer.ToString();
                    if (!string.IsNullOrWhiteSpace(captured))
                        console.Write(new Text(captured.TrimEnd()));
                }
                logBuffer.Dispose();
            }
        }

        // Stop diagnostics stream and telemetry polling.
        await followCts.CancelAsync();
        try { await diagnosticsTask; } catch (OperationCanceledException) { }
        try { await telemetryTask; } catch (OperationCanceledException) { }

        // Flush buffered diagnostics now that the Progress() renderer has released the console.
        while (diagnosticsBuffer.TryDequeue(out var line))
            console.MarkupLine(line);

        if (jobFailed)
            return 1;

        if (lastEvt is not null)
            ShowSuccess(console, $"Export complete — {completed} exported / {skipped} skipped / {revisions} revisions written to package.");
        else
            ShowSuccess(console, "Work item export complete.");
        return 0;
    }

    /// <summary>
    /// Builds the fixed 3-row Live renderable:
    ///   Row 1 — overall WorkItems progress bar
    ///   Row 2 — last completed work item (full bar, greyed)
    ///   Row 3 — current work item in progress (partial bar)
    ///   Row 4 — checkpoint safety indicator
    /// Row count is always exactly 4 so Live()'s cursor-up stays stable.
    /// </summary>
    private static IRenderable BuildProgressRenderable(
        int completed, int skipped, int total,
        int currentWiId, int currentWiRevisions,
        int lastCompletedWiId, int lastCompletedRevisions,
        string stage, string? lastWiStatus = null,
        DateTimeOffset? lastCheckpointAt = null, DateTimeOffset? nextCheckpointDueAt = null,
        double lastRevDurationMs = 0, double avgRevDurationMs = 0,
        int totalRevisions = 0,
        int attProcessed = 0, int attFailed = 0,
        double avgAttDurationMs = 0, long avgAttSizeBytes = 0,
        string? currentAttName = null,
        TeamsCounters? teams = null,
        NodesCounters? nodes = null,
        IdentitiesCounters? identities = null)
    {
        const int BarWidth = 38;
        int processed = completed + skipped;

        // Estimate total revisions — base only on completed (exported) items, not skipped.
        int estimatedTotalRevisions = completed > 0 && total > 0
            ? (int)((double)totalRevisions / completed * total)
            : 0;

        // ── Row 1: work items progress bar (parent) ──────────────────────────────────
        // Bar position = processed (completed + skipped); counts shown separately.
        var wiPct = total > 0 ? (double)processed / total : 0.0;
        var wiFilled = Math.Clamp((int)(wiPct * BarWidth), 0, BarWidth);
        var wiBar = new string('━', wiFilled) + new string('─', BarWidth - wiFilled);
        var stageStr = string.IsNullOrEmpty(stage) ? string.Empty : $"  [grey]{Markup.Escape(stage)}[/]";
        var etaStr = ComputeRevisionEta(totalRevisions, estimatedTotalRevisions, avgRevDurationMs);
        var exportedStr = skipped > 0 ? $"  [green]{completed:N0} exported[/]" : string.Empty;
        var skippedStr = skipped > 0 ? $"  [grey]{skipped:N0} skipped[/]" : string.Empty;
        var wiRow = new Markup(
            $"[bold]WorkItems[/]{stageStr}  [blue]{Markup.Escape(wiBar)}[/]"
            + $"  [bold]{processed:N0}[/][grey]/{total:N0}[/]{exportedStr}{skippedStr}"
            + $"  [grey]{wiPct * 100.0:F1}%[/]  [grey]ETA: {Markup.Escape(etaStr)}[/]");

        // ── Row 2: current work item / fast-forward indicator ─────────────────────────
        IRenderable revRow;
        var isResuming = string.Equals(stage, "Resuming", StringComparison.OrdinalIgnoreCase);
        if (isResuming && currentWiId > 0)
        {
            revRow = new Markup(
                $"  [grey]↳ Resuming WI {currentWiId}[/]  [grey](fast-forwarding {skipped:N0}/{total:N0})[/]");
        }
        else if (currentWiId > 0)
        {
            int refRevs = lastCompletedRevisions > 0 ? lastCompletedRevisions : currentWiRevisions;
            var curPct = refRevs > 0 ? Math.Min(1.0, (double)currentWiRevisions / refRevs) : 0.0;
            var curFilled = Math.Clamp((int)(curPct * BarWidth), 0, BarWidth);
            var curBar = new string('━', curFilled) + new string('─', BarWidth - curFilled);
            var lastRevStr = lastCompletedRevisions > 0 ? $"  [grey](prev: {lastCompletedRevisions} rev)[/]" : string.Empty;
            var statusBadge = lastWiStatus == "Exported" ? " [green]✓[/]"
                            : lastWiStatus == "Failed" ? " [red]✗[/]"
                            : string.Empty;
            revRow = new Markup(
                $"  [grey]↳ WI {currentWiId}[/]{statusBadge}   [blue]{Markup.Escape(curBar)}[/]"
                + $"  [bold]{currentWiRevisions:N0}[/][grey] rev[/]"
                + $"{lastRevStr}");
        }
        else
        {
            revRow = new Markup("  [grey]↳ Revisions   waiting…[/]");
        }

        // ── Row 3: revision timing / back-off indicator ──────────────────────────────
        // Suppressed during fast-forward — no revision work happening, stale values
        // would produce false back-off warnings.
        IRenderable timingRow;
        if (isResuming)
        {
            timingRow = new Markup("  [grey]─ (fast-forwarding, no timing)[/]");
        }
        else if (lastRevDurationMs > 0)
        {
            var lastSec = lastRevDurationMs / 1000.0;
            var avgSec = avgRevDurationMs / 1000.0;
            var isSlowdown = avgRevDurationMs > 0 && lastRevDurationMs > avgRevDurationMs * 3;
            var durationColor = isSlowdown ? "red" : lastRevDurationMs > avgRevDurationMs * 1.5 ? "yellow" : "green";
            var throttleWarning = isSlowdown ? "  [red bold]⚠ possible back-off[/]" : string.Empty;
            timingRow = new Markup(
                $"  [grey]last:[/] [{durationColor}]{lastSec:F1}s[/]" +
                $"  [grey]avg:[/] [grey]{avgSec:F1}s[/]{throttleWarning}");
        }
        else
        {
            timingRow = new Markup("  [grey]─[/]");
        }

        // ── Row 4: checkpoint safety indicator ───────────────────────────────────────
        IRenderable checkpointRow;
        if (nextCheckpointDueAt is null && lastCheckpointAt is not null)
            checkpointRow = new Markup("  [green]✓ Safe to cancel — checkpointed per revision[/]");
        else if (nextCheckpointDueAt is not null)
        {
            var remaining = nextCheckpointDueAt.Value - DateTimeOffset.UtcNow;
            checkpointRow = remaining <= TimeSpan.Zero
                ? new Markup("  [green]✓ Save point due now[/]")
                : new Markup($"  [yellow]⏳ Next save in {(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s[/]");
        }
        else
            checkpointRow = new Markup("  [grey]─[/]");

        // ── Row 5: attachments summary ────────────────────────────────────────────────
        IRenderable attachmentRow;
        if (attProcessed > 0 || attFailed > 0 || currentAttName != null)
        {
            var avgDurStr = avgAttDurationMs > 0 ? $"  [grey]avg dl:[/] [white]{avgAttDurationMs / 1000.0:F1}s[/]" : string.Empty;
            var avgSzStr = avgAttSizeBytes > 0 ? $"  [grey]avg size:[/] [white]{FormatBytes(avgAttSizeBytes)}[/]" : string.Empty;
            var failStr = attFailed > 0 ? $"  [red]{attFailed} failed[/]" : string.Empty;
            var inFlightStr = currentAttName != null
                ? $"  [yellow]↓ {Markup.Escape(TruncateName(currentAttName, 28))}[/]"
                : string.Empty;
            attachmentRow = new Markup(
                $"  [grey]Attachments:[/] [bold]{attProcessed:N0}[/][grey] done[/]{failStr}{inFlightStr}{avgDurStr}{avgSzStr}");
        }
        else
        {
            attachmentRow = new Markup("  [grey]Attachments   –[/]");
        }

        // ── Row 6: identities ─────────────────────────────────────────────────────────
        IRenderable identitiesRow;
        if (identities is not null)
        {
            var unresolvedStr = identities.Unresolved > 0 ? $"  [yellow]{identities.Unresolved:N0} unresolved[/]" : string.Empty;
            var idFailStr = identities.Failed > 0 ? $"  [red]{identities.Failed:N0} failed[/]" : string.Empty;
            identitiesRow = new Markup(
                $"  [grey]Identities:[/] [bold]{identities.Exported:N0}[/][grey] exported[/]"
                + $"  [bold]{identities.Resolved:N0}[/][grey] resolved[/]"
                + $"{unresolvedStr}{idFailStr}");
        }
        else
        {
            identitiesRow = new Markup("  [grey]Identities   –[/]");
        }

        // ── Row 7: nodes ──────────────────────────────────────────────────────────────
        IRenderable nodesRow;
        if (nodes is not null)
        {
            var nodesFailStr = nodes.Failed > 0 ? $"  [red]{nodes.Failed:N0} failed[/]" : string.Empty;
            nodesRow = new Markup(
                $"  [grey]Nodes:[/] [bold]{nodes.AreaPathsReplicated:N0}[/][grey] area[/]"
                + $"  [bold]{nodes.IterationPathsReplicated:N0}[/][grey] iteration[/]"
                + $"{nodesFailStr}");
        }
        else
        {
            nodesRow = new Markup("  [grey]Nodes        –[/]");
        }

        // ── Row 8: teams ──────────────────────────────────────────────────────────────
        IRenderable teamsRow;
        if (teams is not null)
        {
            var teamsFailStr = teams.Failed > 0 ? $"  [red]{teams.Failed:N0} failed[/]" : string.Empty;
            teamsRow = new Markup(
                $"  [grey]Teams:[/] [bold]{teams.Exported:N0}[/][grey] exported[/]"
                + $"  [bold]{teams.Imported:N0}[/][grey] imported[/]"
                + $"  [grey]{teams.Members:N0} members[/]"
                + $"{teamsFailStr}");
        }
        else
        {
            teamsRow = new Markup("  [grey]Teams        –[/]");
        }

        return new Rows(wiRow, revRow, timingRow, attachmentRow, checkpointRow, identitiesRow, nodesRow, teamsRow);
    }

    private static string ComputeRevisionEta(int revisionsWritten, int estimatedTotalRevisions, double avgRevDurationMs)
    {
        if (avgRevDurationMs <= 0 || estimatedTotalRevisions <= 0 || revisionsWritten <= 0)
            return "--:--:--";
        var remainingRevisions = Math.Max(0, estimatedTotalRevisions - revisionsWritten);
        var remainingSecs = remainingRevisions * avgRevDurationMs / 1000.0;
        var eta = TimeSpan.FromSeconds(remainingSecs);
        return eta.TotalHours >= 1
            ? $"{(int)eta.TotalHours}:{eta.Minutes:D2}:{eta.Seconds:D2}"
            : $"--:{eta.Minutes:D2}:{eta.Seconds:D2}";
    }

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };

    private static string TruncateName(string name, int maxLen) =>
        name.Length <= maxLen ? name : "…" + name[^(maxLen - 1)..];

    /// <summary>
    /// Converts <see cref="MigrationOptions.Modules"/> into <see cref="JobModule"/> entries.
    /// Reads from the typed static module configuration and builds the runtime job contract.
    /// </summary>
    private static List<JobModule> BuildModules(MigrationOptions config)
    {
        var modules = new List<JobModule>();
        var wi = config.Modules.WorkItems;

        if (wi.Enabled)
        {
            var scopes = new List<JobModuleScope>();

            // WIQL query scope
            scopes.Add(new JobModuleScope
            {
                Type = "wiql",
                Parameters = new Dictionary<string, object?>
                {
                    ["query"] = wi.Scope.Query
                }
            });

            // Filter scopes
            foreach (var f in wi.Scope.Filters)
            {
                scopes.Add(new JobModuleScope
                {
                    Type = "filter",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["mode"] = f.Mode.ToString().ToLowerInvariant(),
                        ["field"] = f.Field,
                        ["pattern"] = f.Pattern,
                    }
                });
            }

            var ext = wi.Extensions;
            var extensions = new List<JobModuleExtension>
            {
                new() { Type = "Revisions",      Enabled = ext.Revisions.Enabled },
                new() { Type = "Links",          Enabled = ext.Links.Enabled },
                new() { Type = "Attachments",    Enabled = ext.Attachments.Enabled },
                new() { Type = "Comments",       Enabled = ext.Comments.Enabled,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["includeDeleted"] = ext.Comments.IncludeDeleted
                    }
                },
                new() { Type = "EmbeddedImages", Enabled = ext.EmbeddedImages.Enabled,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["downloadTimeoutSeconds"] = ext.EmbeddedImages.DownloadTimeoutSeconds
                    }
                },
                new() { Type = "WorkItemResolutionStrategy", Enabled = ext.WorkItemResolutionStrategy.Enabled,
                    Parameters = new Dictionary<string, object?>
                    {
                        ["strategy"] = ext.WorkItemResolutionStrategy.Strategy,
                        ["fieldName"] = ext.WorkItemResolutionStrategy.FieldName,
                        ["urlPattern"] = ext.WorkItemResolutionStrategy.UrlPattern,
                    }
                },
            };

            modules.Add(new JobModule
            {
                Name = "WorkItems",
                Scopes = scopes,
                Extensions = extensions,
            });
        }

        return modules;
    }
}
