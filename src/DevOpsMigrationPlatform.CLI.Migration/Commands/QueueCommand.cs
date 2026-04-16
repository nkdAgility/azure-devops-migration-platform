using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.CLI.Views;
using DevOpsMigrationPlatform.Infrastructure.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Submits a migration job to the control plane. Behaviour (export, import, or full
/// lifecycle) is determined by the <c>mode</c> field in the configuration file.
///
/// When <c>mode</c> is <c>Export</c> and <c>config.Source.Type == "TeamFoundationServer"</c>,
/// the command transparently delegates to <see cref="TfsExportRunner"/> (the net481
/// subprocess bridge). This keeps TFS OM handling invisible to the operator.
///
/// No migration logic runs in this command — all execution happens in the agent
/// (Azure DevOps path) or the TFS subprocess (TFS path).
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

            services.AddTransient<IJobRunner>(sp => sp.GetRequiredService<ControlPlaneClient>());

            // Pre-flight work item counting — uses the same date-window WIQL strategy as export.
            services.AddExportPreflightServices();

            // TFS subprocess services — registered unconditionally so DI resolves them
            // correctly when the source type is TeamFoundationServer.
            services.AddSingleton<IProgressSink, AnsiProgressSink>();
            services.AddSingleton<TfsExporterProcessAdapter>();
            services.AddSingleton<IExternalToolRunner, ExternalToolRunner>();
        });

        var config = await LoadConfigurationAsync(settings, cancellationToken);
        if (config is null)
            return 1;

        // Route to the appropriate handler based on config mode.
        return config.Mode switch
        {
            "Export" => await ExecuteExportAsync(config, settings, cancellationToken),
            "Import" => await ExecuteImportAsync(config, settings, cancellationToken),
            "Both" => ExecuteMigrateStub(),
            _ => ExecuteInvalidMode(config.Mode)
        };
    }

    private async Task<int> ExecuteImportAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();

        var orgUrl = config.Target?.ResolvedUrl;
        var project = config.Target?.Project;
        var packagePath = config.Artefacts?.ExpandedPath;

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
            ShowError(console, "Artefacts.WorkingDirectory is required for import. Set it in the config file.");
            return 1;
        }

        var outputPath = Path.GetFullPath(packagePath);
        console.MarkupLine($"[blue]ℹ[/] Importing into [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path   : [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);

        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            Mode = "Import",
            Target = new JobEndpoint
            {
                Type = config.Target!.Type,
                Url = orgUrl,
                Project = project,
                ApiVersion = config.Target.ApiVersion,
                Authentication = config.Target.Authentication
            },
            Artefacts = new JobArtefacts
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Artefacts!.CreatePackage
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
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; followCts.Cancel(); };

        try
        {
            await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
            {
                lastEvt = evt;
                console.MarkupLine($"[grey]{Markup.Escape(evt.Stage ?? string.Empty)}[/] WI={evt.WorkItemId} ({evt.WorkItemsProcessed} done)");
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

        await followCts.CancelAsync();
        if (jobFailed) return 1;

        if (lastEvt is not null)
            ShowSuccess(console, $"Import complete — {lastEvt.WorkItemsProcessed} work items imported.");
        else
            ShowSuccess(console, "Work item import complete.");
        return 0;
    }

    private int ExecuteImportStub()
    {
        AnsiConsole.MarkupLine("[grey]import — not available in this release.[/]");
        return 1;
    }

    private int ExecuteMigrateStub()
    {
        AnsiConsole.MarkupLine("[grey]migrate — not available in this release.[/]");
        return 1;
    }

    private int ExecuteInvalidMode(string mode)
    {
        AnsiConsole.MarkupLine($"[red]Invalid mode '{Markup.Escape(mode)}'. Must be Export, Import, or Both.[/]");
        return 1;
    }

    private async Task<int> ExecuteExportAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        // Delegate to the TFS subprocess runner for on-premises sources.
        if (string.Equals(config.Source?.Type, "TeamFoundationServer", StringComparison.Ordinal))
            return await TfsExportRunner.RunAsync(config, Host!.Services, tfsExportExePathOverride: null, cancellationToken);

        return await ExecuteAdoExportAsync(config, settings, cancellationToken);
    }

    private async Task<int> ExecuteAdoExportAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();

        var orgUrl = config.Source?.ResolvedUrl;
        var project = config.Source?.Project;

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

        var outputPath = Path.GetFullPath(config.Artefacts.ExpandedPath);

        console.MarkupLine($"[blue]ℹ[/] Exporting from [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path  : [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);

        // Pre-flight: count work items so we can show a deterministic progress bar.
        var pat = config.Source!.Authentication?.ResolvedAccessToken ?? string.Empty;
        var baseQuery = modules
            .FirstOrDefault(m => string.Equals(m.Name, "WorkItems", StringComparison.Ordinal))
            ?.Scopes.FirstOrDefault(s => string.Equals(s.Type, "wiql", StringComparison.OrdinalIgnoreCase))
            ?.Parameters.TryGetValue("query", out var _q) == true ? _q?.ToString() : null;

        // Validate WIQL query for safety and correctness before execution
        var validationResult = WiqlValidator.Validate(baseQuery);
        if (!validationResult.IsValid)
        {
            ShowError(console, $"Invalid WIQL query: {validationResult.ErrorMessage}");
            return 1;
        }

        var totalWorkItems = 0;
        var discovery = GetRequiredService<IWorkItemDiscoveryService>();

        await console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Plain)
            .StartAsync("[grey]Counting work items…[/]", async _ =>
            {
                await foreach (var snapshot in discovery.CountWorkItemsAsync(
                    orgUrl, project, pat, baseQuery, cancellationToken))
                {
                    if (snapshot.IsWorkItemComplete)
                        totalWorkItems = snapshot.WorkItemsCount;
                }
            });

        if (totalWorkItems > 0)
            console.MarkupLine($"[blue]ℹ[/] Work items found: [bold]{totalWorkItems:N0}[/]");

        // Build MigrationJob — no migration logic here.
        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            Mode = "Export",
            Source = new JobEndpoint
            {
                Type = config.Source!.Type,
                Url = orgUrl,
                Project = project,
                ApiVersion = config.Source.ApiVersion,
                Authentication = config.Source.Authentication
            },
            Artefacts = new JobArtefacts
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Artefacts.CreatePackage
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

        // Register Ctrl+C handler for graceful detach.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent process exit.
            followCts.Cancel();
        };

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
        int lastWiId = 0;
        int wiRevisions = 0;
        int wiStartRevisions = 0;
        int prevRevisions = 0;
        int lastCompletedWiId = 0;
        int lastCompletedRevisions = 0;
        string currentStage = string.Empty;

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
                    if (evt.WorkItemId != 0 && evt.WorkItemId != lastWiId)
                    {
                        if (lastWiId != 0)
                        {
                            lastCompletedWiId = lastWiId;
                            lastCompletedRevisions = prevRevisions - wiStartRevisions;
                        }
                        lastWiId = evt.WorkItemId;
                        wiStartRevisions = prevRevisions;
                    }
                    if (evt.WorkItemId != 0)
                        wiRevisions = evt.RevisionsProcessed - wiStartRevisions;
                    prevRevisions = evt.RevisionsProcessed;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
            {
                jobFailed = true;
                ShowError(console, ex.Message);
                if (lastEvt is not null)
                    ShowError(console, $"Last progress: {lastEvt.WorkItemsProcessed} work items / {lastEvt.RevisionsProcessed} revisions (wi#{lastEvt.WorkItemId})");
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
            Console.SetOut(logBuffer);

            try
            {
                await console.Live(BuildProgressRenderable(
                        0, totalWorkItems, 0, 0, 0, 0, string.Empty, progressStartTime))
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .StartAsync(async ctx =>
                    {
                        await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
                        {
                            lastEvt = evt;

                            if (!string.IsNullOrEmpty(evt.Stage))
                                currentStage = evt.Stage;

                            if (evt.WorkItemId != 0 && evt.WorkItemId != lastWiId)
                            {
                                // The previous WI just completed — record it for the completed row.
                                if (lastWiId != 0)
                                {
                                    lastCompletedWiId = lastWiId;
                                    lastCompletedRevisions = prevRevisions - wiStartRevisions;
                                }
                                lastWiId = evt.WorkItemId;
                                wiStartRevisions = prevRevisions;
                            }

                            if (evt.WorkItemId != 0)
                                wiRevisions = evt.RevisionsProcessed - wiStartRevisions;

                            prevRevisions = evt.RevisionsProcessed;

                            ctx.UpdateTarget(BuildProgressRenderable(
                                evt.WorkItemsProcessed, totalWorkItems,
                                lastWiId, wiRevisions,
                                lastCompletedWiId, lastCompletedRevisions,
                                currentStage, progressStartTime));
                        }
                    });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
            {
                jobFailed = true;
                ShowError(console, ex.Message);
                if (lastEvt is not null)
                    ShowError(console, $"Last progress: {lastEvt.WorkItemsProcessed} work items / {lastEvt.RevisionsProcessed} revisions (wi#{lastEvt.WorkItemId})");
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C pressed — detach.
                console.MarkupLine("[yellow]Detached from diagnostic stream. Job continues running on the server.[/]");
                console.MarkupLine("[grey]Use [bold]tui[/] to resume watching.[/]");
                return 0;
            }
            finally
            {
                // Restore stdout and flush anything the Console logger wrote during the live render.
                Console.SetOut(originalOut);
                var captured = logBuffer.ToString();
                logBuffer.Dispose();
                if (!string.IsNullOrWhiteSpace(captured))
                    console.Write(new Text(captured.TrimEnd()));
            }
        }

        // Stop diagnostics stream.
        await followCts.CancelAsync();
        try { await diagnosticsTask; } catch (OperationCanceledException) { }

        // Flush buffered diagnostics now that the Progress() renderer has released the console.
        while (diagnosticsBuffer.TryDequeue(out var line))
            console.MarkupLine(line);

        if (jobFailed)
            return 1;

        if (lastEvt is not null)
            ShowSuccess(console, $"Export complete — {lastEvt.WorkItemsProcessed} work items / {lastEvt.RevisionsProcessed} revisions written to package.");
        else
            ShowSuccess(console, "Work item export complete.");
        return 0;
    }

    /// <summary>
    /// Builds the fixed 3-row Live renderable:
    ///   Row 1 — overall WorkItems progress bar
    ///   Row 2 — last completed work item (full bar, greyed)
    ///   Row 3 — current work item in progress (partial bar)
    /// Row count is always exactly 3 so Live()'s cursor-up stays stable.
    /// </summary>
    private static IRenderable BuildProgressRenderable(
        int processed, int total,
        int currentWiId, int currentWiRevisions,
        int lastCompletedWiId, int lastCompletedRevisions,
        string stage, DateTimeOffset startTime)
    {
        const int BarWidth = 38;
        const int WiBarWidth = 20;

        // ── Row 1: overall progress ──────────────────────────────────────────────────
        var pct = total > 0 ? (double)processed / total : 0.0;
        var filled = Math.Clamp((int)(pct * BarWidth), 0, BarWidth);
        var overallBar = new string('━', filled) + new string('─', BarWidth - filled);
        var stageStr = string.IsNullOrEmpty(stage) ? string.Empty : $"  [grey]{Markup.Escape(stage)}[/]";
        var etaStr = ComputeEta(startTime, processed, total);
        var overall = new Markup(
            $"[bold]WorkItems[/]{stageStr}  [blue]{Markup.Escape(overallBar)}[/]" +
            $"  [bold]{processed:N0}[/][grey]/{total:N0}[/]" +
            $"  [grey]{pct * 100.0:F1}%[/]  [grey]ETA: {Markup.Escape(etaStr)}[/]");

        // ── Row 2: last completed WI (full bar = 100 %) ──────────────────────────────
        IRenderable completedRow = lastCompletedWiId > 0
            ? new Markup($"  [grey]✓ WI {lastCompletedWiId}  {new string('━', WiBarWidth)}  {lastCompletedRevisions} rev  done[/]")
            : new Markup("  [grey]─[/]");

        // ── Row 3: current WI in progress (partial bar, never reaches 100 %) ─────────
        IRenderable currentRow;
        if (currentWiId > 0)
        {
            // Bar fills as revisions arrive; capped one below full so it never
            // reads as 100 % while the item is still being processed.
            var wiBarFilled = Math.Min(currentWiRevisions, WiBarWidth - 1);
            var wiBar = new string('━', wiBarFilled) + new string('─', WiBarWidth - wiBarFilled);
            currentRow = new Markup(
                $"  [grey]↳[/] WI [bold]{currentWiId}[/]  [blue]{Markup.Escape(wiBar)}[/]  [grey]{currentWiRevisions} rev[/]");
        }
        else
        {
            currentRow = new Markup("  [grey]↳ waiting…[/]");
        }

        return new Rows(overall, completedRow, currentRow);
    }

    private static string ComputeEta(DateTimeOffset startTime, int processed, int total)
    {
        if (processed <= 0 || total <= 0) return "--:--:--";
        var elapsedSecs = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
        if (elapsedSecs < 1) return "--:--:--";
        var remainingSecs = (total - processed) / (processed / elapsedSecs);
        var eta = TimeSpan.FromSeconds(remainingSecs);
        return eta.TotalHours >= 1
            ? $"{(int)eta.TotalHours}:{eta.Minutes:D2}:{eta.Seconds:D2}"
            : $"--:{eta.Minutes:D2}:{eta.Seconds:D2}";
    }

    /// <summary>
    /// Converts <see cref="MigrationOptions.Modules"/> into <see cref="JobModule"/> entries.
    /// If the config has no modules, a default WorkItems module with all extensions enabled is injected
    /// to preserve backward compatibility with pre-modules config files.
    /// </summary>
    private static List<JobModule> BuildModules(MigrationOptions config)
    {
        if (config.Modules.Count > 0)
        {
            return config.Modules
                .Where(m => m.Enabled)
                .Select(m => new JobModule
                {
                    Name = m.Name,
                    Scopes = m.Scopes
                        .Select(s => new JobModuleScope
                        {
                            Type = s.Type,
                            Parameters = s.Parameters
                                .ToDictionary(
                                    kv => kv.Key,
                                    kv => (object?)kv.Value.ToString())
                        })
                        .ToList(),
                    Extensions = m.Extensions
                        .Select(e => new JobModuleExtension
                        {
                            Type = e.Type,
                            Enabled = e.Enabled,
                            Parameters = e.Parameters
                                .ToDictionary(
                                    kv => kv.Key,
                                    kv => (object?)kv.Value.ToString())
                        })
                        .ToList()
                })
                .ToList();
        }

        // Default: WorkItems module with all extensions enabled.
        return
        [
            new JobModule
            {
                Name = "WorkItems",
                Scopes =
                [
                    new JobModuleScope
                    {
                        Type = "wiql",
                        Parameters = new Dictionary<string, object?>
                        {
                            ["query"] = WorkItemsModuleExtensions.DefaultWiqlQuery
                        }
                    }
                ],
                Extensions =
                [
                    new JobModuleExtension { Type = "Revisions",      Enabled = true },
                    new JobModuleExtension { Type = "Links",          Enabled = true },
                    new JobModuleExtension { Type = "Attachments",    Enabled = true },
                    new JobModuleExtension { Type = "Comments",       Enabled = true },
                    new JobModuleExtension { Type = "EmbeddedImages", Enabled = true },
                ]
            }
        ];
    }
}
