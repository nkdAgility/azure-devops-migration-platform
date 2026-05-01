using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.CLI.Views;
using DevOpsMigrationPlatform.Infrastructure.Config;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Abstractions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Abstractions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;
using MsOptions = Microsoft.Extensions.Options.Options;

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

            // T033: Register IConfigSchemaValidator with schema path
            services.AddSingleton<IOptions<JsonSchemaConfigValidatorOptions>>(sp =>
                MsOptions.Create(new JsonSchemaConfigValidatorOptions
                {
                    SchemaPath = Path.Combine(AppContext.BaseDirectory, "migration.schema.json")
                }));
            services.AddSingleton<IConfigSchemaValidator, JsonSchemaConfigValidator>();

        });

        // T034: Validate config against schema before loading
        var configPath = GetConfigurationPath(settings);
        if (configPath != null)
        {
            var schemaValidator = GetRequiredService<IConfigSchemaValidator>();
            var logger = GetRequiredService<ILogger<QueueCommand>>();
            var console = GetRequiredService<IAnsiConsole>();

            try
            {
                var rawJson = await File.ReadAllTextAsync(Path.GetFullPath(configPath), cancellationToken);
                var validationErrors = schemaValidator.Validate(rawJson);

                if (validationErrors.Count > 0)
                {
                    console.MarkupLine("[red]Configuration validation failed:[/]");
                    foreach (var error in validationErrors)
                    {
                        logger.LogError(
                            "Schema validation error at {JsonPath}: {Constraint} in {ConfigFile}",
                            error.JsonPath,
                            error.Constraint,
                            configPath);
                        console.MarkupLine($"  [red]•[/] {error.JsonPath}: {error.Constraint}");
                    }
                    return 1; // Non-zero exit
                }
            }
            catch (FileNotFoundException ex) when (ex.Message.Contains("migration.schema.json"))
            {
                // Schema file absent - log warning and proceed
                var expectedSchemaPath = Path.Combine(AppContext.BaseDirectory, "migration.schema.json");
                logger.LogWarning(
                    "Schema file not found at {ExpectedSchemaPath}. Validation skipped.",
                    expectedSchemaPath);
                console.MarkupLine($"[yellow]⚠[/] Schema file not found at {Markup.Escape(expectedSchemaPath)}. Validation skipped.");
            }
        }

        var config = await LoadConfigurationAsync(settings, cancellationToken);
        if (config is null)
            return 1;

        // Route to the appropriate handler based on config mode.
        return config.Mode switch
        {
            "Export" => await ExecuteExportAsync(config, settings, cancellationToken),
            "Prepare" => await ExecutePrepareAsync(config, settings, cancellationToken),
            "Import" => await ExecuteImportAsync(config, settings, cancellationToken),
            "Migrate" => await ExecuteMigrateAsync(config, settings, cancellationToken),
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
            PackagePathResolver.ExtractOrgFolderName(orgUrl),
            project);
        console.MarkupLine($"[blue]ℹ[/] Importing into [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path   : [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);
        var configPayload = await File.ReadAllTextAsync(Path.GetFullPath(GetConfigurationPath(settings) ?? settings.ConfigFile!), cancellationToken);

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Import,
            ConfigPayload = configPayload,
            Connectors = GetConnectors(config),
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Package!.CreatePackage
            },
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

    private async Task<int> ExecuteMigrateAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var exportResult = await ExecuteExportAsync(config, settings, cancellationToken);
        if (exportResult != 0)
            return exportResult;

        var prepareResult = await ExecutePrepareAsync(config, settings, cancellationToken);
        if (prepareResult != 0)
            return prepareResult;

        return await ExecuteImportAsync(config, settings, cancellationToken);
    }

    private async Task<int> ExecutePrepareAsync(MigrationOptions config, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();

        var packagePath = config.Package?.ExpandedPath;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            ShowError(console, "Package.WorkingDirectory is required for prepare. Set it in the config file.");
            return 1;
        }

        var outputPath = Path.GetFullPath(packagePath);
        console.MarkupLine($"[blue]ℹ[/] Preparing package at [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);
        var configPayload = await File.ReadAllTextAsync(Path.GetFullPath(GetConfigurationPath(settings) ?? settings.ConfigFile!), cancellationToken);

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Prepare,
            ConfigPayload = configPayload,
            Connectors = GetConnectors(config),
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Package!.CreatePackage
            },
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

        ShowSuccess(console, "Prepare phase complete.");
        return 0;
    }

    private static ConnectorType[] GetConnectors(MigrationOptions config)
    {
        var connectors = new System.Collections.Generic.HashSet<ConnectorType>();
        void AddForType(string? type)
        {
            if (string.Equals(type, "TeamFoundationServer", StringComparison.OrdinalIgnoreCase))
                connectors.Add(ConnectorType.TeamFoundationServer);
            else if (string.Equals(type, "AzureDevOpsServices", StringComparison.OrdinalIgnoreCase))
                connectors.Add(ConnectorType.AzureDevOps);
        }
        AddForType(config.Source?.Type);
        AddForType(config.Target?.Type);
        return connectors.Count > 0 ? connectors.ToArray() : Array.Empty<ConnectorType>();
    }

    private int ExecuteInvalidMode(string mode)
    {
        AnsiConsole.MarkupLine($"[red]Invalid mode '{Markup.Escape(mode)}'. Must be Export, Prepare, Import, or Migrate.[/]");
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
            PackagePathResolver.ExtractOrgFolderName(orgUrl),
            project);

        console.MarkupLine($"[blue]ℹ[/] Exporting from [bold]Simulated[/] source");
        console.MarkupLine($"[blue]ℹ[/] Package path  : [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);

        // Config payload is transported in the Job so the agent can write it to the package.
        var configPayload = await File.ReadAllTextAsync(Path.GetFullPath(GetConfigurationPath(settings) ?? settings.ConfigFile!), cancellationToken);

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Export,
            ConfigPayload = configPayload,
            Connectors = GetConnectors(config),
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Package.CreatePackage
            },
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
            PackagePathResolver.ExtractOrgFolderName(orgUrl),
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

        var configPayload = await File.ReadAllTextAsync(Path.GetFullPath(GetConfigurationPath(settings) ?? settings.ConfigFile!), cancellationToken);

        // Build Job — no migration logic here.
        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Export,
            ConfigPayload = configPayload,
            Connectors = GetConnectors(config),
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = config.Package.CreatePackage
            },
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
        var updates = Channel.CreateUnbounded<JobProgressUpdate>();
        var state = JobProgressState.Initial(totalWorkItems);

        // Bootstrap poller: fires once when the agent pushes its execution plan.
        // Also resolves the LastEventSequence so the SSE subscriber can use it as Last-Event-ID.
        var lastEventSequenceTcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bootstrapTask = Task.Run(() => PollForBootstrap(client, parsedJobId, updates.Writer, lastEventSequenceTcs, followCts.Token));

        // Channel 2: telemetry polling — pushes TelemetryPolled updates every 5 s.
        // Channel 1: SSE stage stream — pushes StageAdvanced and JobTerminated updates.
        // Both producers write into the same channel; the consumer applies each update
        // via Apply() and re-renders, keeping all state in one immutable record.
        var telemetryTask = Task.Run(() => FetchLatestMetrics(client, parsedJobId, updates.Writer, followCts.Token));
        var progressTask = Task.Run(async () =>
        {
            // Wait for the bootstrap poller to capture LastEventSequence before starting SSE
            // so we don't replay events the bootstrap already described. The poller resolves
            // within 1 s of the agent starting; fall back to 0 if it times out first.
            long lastSeq = 0;
            try { lastSeq = await lastEventSequenceTcs.Task.WaitAsync(TimeSpan.FromSeconds(65), followCts.Token).ConfigureAwait(false); }
            catch { /* cancelled or timed out — start SSE from beginning */ }
            await FollowJobProgress(client, parsedJobId, updates.Writer, followCts.Token, lastSeq).ConfigureAwait(false);
        });

        if (Console.IsOutputRedirected)
        {
            // Non-interactive (redirected stdout — subprocess, CI, test runner): skip the
            // Live renderer entirely.  Cursor-positioning ANSI sequences throw "The handle
            // is invalid" on non-console handles; plain event iteration is sufficient.
            try
            {
                await foreach (var update in updates.Reader.ReadAllAsync(followCts.Token).ConfigureAwait(false))
                {
                    state = Apply(state, update);
                    // In non-interactive mode emit task transitions as plain log lines.
                    if (update is TaskListReceived tlr && tlr.Tasks.Tasks.Count > 0)
                    {
                        console.MarkupLine("[grey]Execution plan:[/]");
                        foreach (var t in tlr.Tasks.Tasks.OrderBy(t => t.Order))
                            console.MarkupLine($"[grey]  · {Markup.Escape(t.Name)}[/]");
                    }
                    if (update is TaskUpdated tu)
                    {
                        var icon = tu.Status switch
                        {
                            JobTaskStatus.Running => "⠋",
                            JobTaskStatus.Completed => "✓",
                            JobTaskStatus.Failed => "✗",
                            JobTaskStatus.Skipped => "→",
                            _ => "·"
                        };
                        var taskName = state.Tasks?.Tasks.FirstOrDefault(t => t.Id == tu.TaskId)?.Name ?? tu.TaskId;
                        console.MarkupLine($"[grey]{icon} {Markup.Escape(taskName)}[/]");
                    }
                    if (update is JobTerminated jt)
                    {
                        if (jt.Failed)
                        {
                            jobFailed = true;
                            ShowError(console, jt.Reason ?? "Job failed");
                            ShowError(console, $"Last progress: {state.Completed} exported / {state.Skipped} skipped / {state.Revisions} revisions");
                        }
                        break;
                    }
                }
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
                await console.Live(BuildProgressDisplay(state))
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .StartAsync(async ctx =>
                    {
                        // Spectre.Console Live does NOT render until the callback
                        // calls Refresh/UpdateTarget. Force the initial render.
                        ctx.Refresh();

                        await foreach (var update in updates.Reader.ReadAllAsync(followCts.Token).ConfigureAwait(false))
                        {
                            state = Apply(state, update);
                            ctx.UpdateTarget(BuildProgressDisplay(state));
                            if (update is JobTerminated jt)
                            {
                                if (jt.Failed)
                                {
                                    jobFailed = true;
                                    ShowError(console, jt.Reason ?? "Job failed");
                                }
                                break;
                            }
                        }
                    });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
            {
                // Normally handled via JobTerminated message from FollowJobProgress — safety net only.
                jobFailed = true;
                ShowError(console, ex.Message);
                ShowError(console, $"Last progress: {state.Completed} exported / {state.Skipped} skipped / {state.Revisions} revisions");
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

        // Stop diagnostics stream, telemetry polling, and progress streaming.
        await followCts.CancelAsync();
        try { await diagnosticsTask; } catch (OperationCanceledException) { }
        try { await telemetryTask; } catch (OperationCanceledException) { }
        try { await progressTask; } catch (OperationCanceledException) { }
        try { await bootstrapTask; } catch (OperationCanceledException) { }

        // Flush buffered diagnostics now that the Progress() renderer has released the console.
        while (diagnosticsBuffer.TryDequeue(out var line))
            console.MarkupLine(line);

        if (jobFailed)
            return 1;

        if (state.LastEvent is not null)
            ShowSuccess(console, $"Export complete — {state.Completed} exported / {state.Skipped} skipped / {state.Revisions} revisions written to package.");
        else
            ShowSuccess(console, "Work item export complete.");
        return 0;
    }

    // ── Job progress state (MVU) ─────────────────────────────────────────────────────────
    // All display state is held in one immutable record updated via Apply().
    // Producers push JobProgressUpdate messages into a Channel; the consumer
    // calls Apply() on each message and re-renders via BuildProgressDisplay().

    private record JobProgressState(
        int Completed,
        int Skipped,
        int TotalWorkItems,
        int Revisions,
        int LastWiRevisions,
        int CurrentWiId,
        int CurrentWiIndex,
        int CurrentWiRevsWritten,
        double LastRevDurationMs,
        double AverageRevDurationMs,
        string Stage,
        int AttachmentsProcessed,
        int AttachmentsFailed,
        double AverageAttachmentDurationMs,
        long AverageAttachmentSizeBytes,
        string? CurrentAttachmentName,
        string? LastWorkItemStatus,
        JobMetrics? Metrics,
        DateTimeOffset? LastCheckpointAt,
        DateTimeOffset? NextCheckpointDueAt,
        ProgressEvent? LastEvent,
        int TeamsCount,
        int NodesCount,
        int IdentitiesCount,
        JobTaskList? Tasks)
    {
        public static JobProgressState Initial(int totalWorkItems) => new(
            0, 0, totalWorkItems, 0, 0, 0, 0, 0, 0, 0,
            string.Empty, 0, 0, 0, 0, null, null, null, null, null, null,
            0, 0, 0, null);
    }

    private abstract record JobProgressUpdate;
    private record TelemetryPolled(JobMetrics Metrics) : JobProgressUpdate;
    private record StageAdvanced(ProgressEvent Event) : JobProgressUpdate;
    private record JobTerminated(bool Failed, string? Reason) : JobProgressUpdate;
    /// <summary>Fired once when the bootstrap poll returns a non-null task list.</summary>
    private record TaskListReceived(JobTaskList Tasks, long LastEventSequence) : JobProgressUpdate;
    /// <summary>Fired per SSE event when <see cref="ProgressEvent.TaskId"/> is set.</summary>
    private record TaskUpdated(string TaskId, JobTaskStatus Status, long? CompletedCount) : JobProgressUpdate;

    private static JobProgressState Apply(JobProgressState state, JobProgressUpdate update) => update switch
    {
        TelemetryPolled t => ApplyTelemetry(state, t.Metrics),
        StageAdvanced t => ApplyStageAdvance(state, t.Event),
        TaskListReceived t => state with { Tasks = t.Tasks },
        TaskUpdated t => ApplyTaskUpdate(state, t.TaskId, t.Status, t.CompletedCount),
        _ => state
    };

    private static JobProgressState ApplyTelemetry(JobProgressState state, JobMetrics metrics)
    {
        var wi = metrics.Migration?.WorkItems;
        var att = wi?.Attachments;
        // Agent resolves the real scope total after querying the source — use it when available,
        // retain the previous value between polls so the display never flashes to 0.
        var scopeTotal = (int)(metrics.Scope?.WorkItemsTotal ?? 0);
        return state with
        {
            Metrics = metrics,
            TotalWorkItems = scopeTotal > 0 ? scopeTotal : state.TotalWorkItems,
            Completed = wi is not null ? (int)wi.Completed : state.Completed,
            Skipped = wi is not null ? (int)wi.Skipped : state.Skipped,
            Revisions = wi is not null && wi.RevisionsProcessed > 0 ? (int)wi.RevisionsProcessed : state.Revisions,
            LastWiRevisions = wi?.LastWorkItemRevisions > 0 ? (int)wi.LastWorkItemRevisions : state.LastWiRevisions,
            LastRevDurationMs = wi?.LastRevisionDurationMs > 0 ? wi.LastRevisionDurationMs : state.LastRevDurationMs,
            AverageRevDurationMs = wi?.AverageRevisionDurationMs > 0 ? wi.AverageRevisionDurationMs : state.AverageRevDurationMs,
            LastWorkItemStatus = wi?.LastWorkItemStatus ?? state.LastWorkItemStatus,
            CurrentWiId = wi?.CurrentWorkItemId > 0 ? wi.CurrentWorkItemId : state.CurrentWiId,
            CurrentWiIndex = wi?.CurrentWorkItemId > 0 ? wi.CurrentWorkItemIndex : state.CurrentWiIndex,
            CurrentWiRevsWritten = wi?.CurrentWorkItemId > 0 ? wi.CurrentWorkItemRevisionsWritten : state.CurrentWiRevsWritten,
            AttachmentsProcessed = att is not null ? (int)att.Processed : state.AttachmentsProcessed,
            AttachmentsFailed = att is not null ? (int)att.Failed : state.AttachmentsFailed,
            AverageAttachmentDurationMs = att is not null ? att.AverageDownloadDurationMs : state.AverageAttachmentDurationMs,
            AverageAttachmentSizeBytes = att is not null ? att.AverageSizeBytes : state.AverageAttachmentSizeBytes,
            CurrentAttachmentName = att?.CurrentAttachmentName ?? state.CurrentAttachmentName,
        };
    }

    private static JobProgressState ApplyTaskUpdate(
        JobProgressState state, string taskId, JobTaskStatus newStatus, long? completedCount)
    {
        if (state.Tasks is null) return state;

        var updatedTasks = new System.Collections.Generic.List<JobTask>(state.Tasks.Tasks.Count);
        foreach (var task in state.Tasks.Tasks)
        {
            if (task.Id != taskId)
            {
                updatedTasks.Add(task);
                continue;
            }
            var now = DateTimeOffset.UtcNow;
            updatedTasks.Add(task with
            {
                Status = newStatus,
                CompletedCount = completedCount ?? task.CompletedCount,
                StartedAt = newStatus == JobTaskStatus.Running ? task.StartedAt ?? now : task.StartedAt,
                CompletedAt = newStatus is JobTaskStatus.Completed or JobTaskStatus.Failed or JobTaskStatus.Skipped
                    ? now
                    : task.CompletedAt,
            });
        }
        return state with { Tasks = state.Tasks with { Tasks = updatedTasks.AsReadOnly() } };
    }

    // Only WorkItems events (or global job-engine events with no module) should update the
    // WorkItems stage label. Events from Teams/Nodes/Identities must not overwrite it.
    private static bool IsWorkItemsOrGlobalModule(ProgressEvent evt) =>
        string.IsNullOrEmpty(evt.Module) || evt.Module == "WorkItems";

    private static JobProgressState ApplyStageAdvance(JobProgressState state, ProgressEvent evt)
    {
        // Merge module completion metrics carried on the SSE event immediately into state
        // so the bar flips to green without waiting for the next telemetry poll.
        JobMetrics? mergedMetrics = state.Metrics;
        if (evt.Metrics is not null)
        {
            var existing = state.Metrics;
            var incoming = evt.Metrics;
            var inMig = incoming.Migration;
            var exMig = existing?.Migration;
            mergedMetrics = (incoming with
            {
                Migration = new MigrationCounters
                {
                    WorkItems = inMig?.WorkItems ?? exMig?.WorkItems ?? new WorkItemCounters(),
                    Teams = inMig?.Teams ?? exMig?.Teams,
                    Nodes = inMig?.Nodes ?? exMig?.Nodes,
                    Identities = inMig?.Identities ?? exMig?.Identities,
                    Diagnostics = inMig?.Diagnostics ?? exMig?.Diagnostics,
                },
                Scope = incoming.Scope ?? existing?.Scope ?? new JobScopeCounters(),
            });
        }

        var afterMetrics = state with
        {
            LastEvent = evt,
            Metrics = mergedMetrics,
            Stage = IsWorkItemsOrGlobalModule(evt) && !string.IsNullOrEmpty(evt.Stage)
                ? evt.Stage
                : state.Stage,
            LastCheckpointAt = evt.LastCheckpointAt ?? state.LastCheckpointAt,
            NextCheckpointDueAt = evt.NextCheckpointDueAt ?? state.NextCheckpointDueAt,
            TeamsCount = evt.Module == "Teams" && evt.Stage == "Teams.Export.Team"
                ? state.TeamsCount + 1
                : state.TeamsCount,
            NodesCount = evt.Module == "Nodes" && evt.Stage?.StartsWith("Nodes.", StringComparison.Ordinal) == true
                ? state.NodesCount + 1
                : state.NodesCount,
            IdentitiesCount = evt.Module == "Identities" && evt.Stage?.StartsWith("Identities.", StringComparison.Ordinal) == true
                ? state.IdentitiesCount + 1
                : state.IdentitiesCount,
        };

        // Apply task status transition derived from the ProgressEvent — same logic as ProgressController.
        if (evt.TaskId is not null && evt.TaskStatus.HasValue)
            return ApplyTaskUpdate(afterMetrics, evt.TaskId, evt.TaskStatus.Value, evt.CompletedCount);

        return afterMetrics;
    }

    /// <summary>Renders the current <see cref="JobProgressState"/> into the Live display.</summary>
    private static IRenderable BuildProgressDisplay(JobProgressState s)
    {
        // Task list panel (shown as soon as bootstrap returns the plan)
        IRenderable? taskPanel = s.Tasks switch
        {
            null => new Markup("[grey]⠋ Initialising — waiting for agent to start…[/]"),
            { Tasks.Count: 0 } => null,
            var tasks => BuildTaskListRenderable(tasks)
        };

        var detailPanel = BuildProgressRenderable(
            s.Completed, s.Skipped, s.TotalWorkItems,
            s.CurrentWiId, s.CurrentWiRevsWritten,
            s.CurrentWiIndex, s.LastWiRevisions,
            s.Stage, s.LastWorkItemStatus,
            s.LastCheckpointAt, s.NextCheckpointDueAt,
            s.LastRevDurationMs, s.AverageRevDurationMs,
            s.Revisions,
            s.AttachmentsProcessed, s.AttachmentsFailed,
            s.AverageAttachmentDurationMs, s.AverageAttachmentSizeBytes, s.CurrentAttachmentName,
            s.Metrics?.Migration?.Teams,
            s.Metrics?.Migration?.Nodes,
            s.Metrics?.Migration?.Identities,
            s.TeamsCount, s.NodesCount, s.IdentitiesCount);

        if (taskPanel is null)
            return detailPanel;

        return new Rows(taskPanel, new Rule(), detailPanel);
    }

    /// <summary>
    /// Renders the ordered task list as a checklist, grouped by phase.
    /// </summary>
    private static IRenderable BuildTaskListRenderable(JobTaskList taskList)
    {
        var rows = new System.Collections.Generic.List<IRenderable>();
        string? currentPhase = null;

        foreach (var task in taskList.Tasks.OrderBy(t => t.Order))
        {
            if (task.Phase != currentPhase)
            {
                currentPhase = task.Phase;
                if (!string.IsNullOrEmpty(currentPhase))
                    rows.Add(new Markup($"[bold grey]{Markup.Escape(currentPhase)}[/]"));
            }

            var (icon, color) = task.Status switch
            {
                JobTaskStatus.Running => (s_spinnerFrames[(int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80 % s_spinnerFrames.Length)], "blue"),
                JobTaskStatus.Completed => ("✓", "green"),
                JobTaskStatus.Failed => ("✗", "red"),
                JobTaskStatus.Skipped => ("→", "grey"),
                _ => ("·", "grey"),
            };

            var countStr = string.Empty;
            if (task.KnownTotal.HasValue && task.KnownTotal > 0)
            {
                var done = task.CompletedCount ?? 0;
                var pct = Math.Clamp((double)done / task.KnownTotal.Value, 0, 1);
                const int barW = 20;
                var filled = (int)(pct * barW);
                var bar = new string('━', filled) + new string('─', barW - filled);
                countStr = $"  [{color}]{Markup.Escape(bar)}[/]  [grey]{done:N0}/{task.KnownTotal.Value:N0}[/]";
            }
            else if (task.CompletedCount.HasValue && task.CompletedCount > 0)
            {
                countStr = $"  [grey]{task.CompletedCount.Value:N0}[/]";
            }

            rows.Add(new Markup($"  [{color}]{icon}[/]  {Markup.Escape(task.Name)}{countStr}"));
        }

        return rows.Count > 0 ? new Rows(rows) : new Markup("[grey]─[/]");
    }

    /// <summary>
    /// Polls <c>GET /jobs/{jobId}/bootstrap</c> at 1-second intervals until the agent
    /// has pushed an execution plan (<see cref="JobBootstrap.Tasks"/> is non-null) or
    /// the timeout of 60 seconds elapses, then writes a single
    /// <see cref="TaskListReceived"/> into <paramref name="updates"/> and exits.
    /// Also captures <see cref="JobBootstrap.LastEventSequence"/> for SSE reconnect.
    /// </summary>
    private static async Task PollForBootstrap(
        IControlPlaneClient client, Guid jobId,
        ChannelWriter<JobProgressUpdate> updates,
        TaskCompletionSource<long> lastEventSequenceTcs,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        try
        {
            while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    var bootstrap = await client.GetBootstrapAsync(jobId, ct).ConfigureAwait(false);
                    if (bootstrap?.Tasks is not null)
                    {
                        lastEventSequenceTcs.TrySetResult(bootstrap.LastEventSequence);
                        await updates.WriteAsync(
                            new TaskListReceived(bootstrap.Tasks, bootstrap.LastEventSequence), ct)
                            .ConfigureAwait(false);
                        return;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception) { /* best-effort — retry on next tick */ }

                await Task.Delay(1_000, ct).ConfigureAwait(false);
            }

            // Timeout — signal with empty list so the display can exit "Initialising" state.
            lastEventSequenceTcs.TrySetResult(0);
            var empty = new JobTaskList { Tasks = Array.Empty<JobTask>(), PushedAt = DateTimeOffset.UtcNow };
            await updates.WriteAsync(new TaskListReceived(empty, 0), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Polls <c>GET /jobs/{jobId}/telemetry</c> every 5 s and pushes
    /// <see cref="TelemetryPolled"/> updates into <paramref name="updates"/>.
    /// </summary>
    private static async Task FetchLatestMetrics(
        IControlPlaneClient client, Guid jobId,
        ChannelWriter<JobProgressUpdate> updates, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var m = await client.GetTelemetryAsync(jobId, ct).ConfigureAwait(false);
                    if (m is not null)
                        await updates.WriteAsync(new TelemetryPolled(m), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception) { /* best-effort — do not propagate */ }
                await Task.Delay(5_000, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Subscribes to the SSE progress stream via <c>GET /jobs/{jobId}/progress?follow=true</c>
    /// and pushes <see cref="StageAdvanced"/> updates (and a terminal <see cref="JobTerminated"/>)
    /// into <paramref name="updates"/>.
    /// </summary>
    private static async Task FollowJobProgress(
        IControlPlaneClient client, Guid jobId,
        ChannelWriter<JobProgressUpdate> updates, CancellationToken ct,
        long lastEventSequence = 0)
    {
        try
        {
            await foreach (var evt in client.FollowLogsAsync(jobId, ct,
                lastEventSequence > 0 ? lastEventSequence : null).ConfigureAwait(false))
                await updates.WriteAsync(new StageAdvanced(evt), ct).ConfigureAwait(false);

            await updates.WriteAsync(new JobTerminated(false, null), ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
        {
            await updates.WriteAsync(new JobTerminated(true, ex.Message), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    // ── Progress renderable (pure render function) ───────────────────────────────────────

    /// <summary>
    /// Builds the fixed 3-row Live renderable:
    ///   Row 1 — overall WorkItems progress bar
    ///   Row 2 — last completed work item (full bar, greyed)
    ///   Row 3 — current work item in progress (partial bar)
    ///   Row 4 — checkpoint safety indicator
    /// Row count is always exactly 4 so Live()'s cursor-up stays stable.
    /// </summary>
    private static readonly string[] s_spinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

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
        IdentitiesCounters? identities = null,
        int teamsCount = 0, int nodesCount = 0, int identitiesCount = 0)
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
        var spinnerFrame = s_spinnerFrames[(int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 100) % s_spinnerFrames.Length];
        IRenderable identitiesRow;
        {
            string idBar; string idBarColor; string idCheck; string idCounts;
            if (identities is not null)
            {
                idBar = new string('━', BarWidth); idBarColor = "green"; idCheck = " [green]✓[/]";
                idCounts = $"  [bold]{identities.Exported:N0}[/][grey] exported[/]"
                    + (identities.Resolved > 0 ? $"  [bold]{identities.Resolved:N0}[/][grey] resolved[/]" : string.Empty)
                    + (identities.Unresolved > 0 ? $"  [yellow]{identities.Unresolved:N0} unresolved[/]" : string.Empty)
                    + (identities.Failed > 0 ? $"  [red]{identities.Failed:N0} failed[/]" : string.Empty);
            }
            else if (identitiesCount > 0)
            {
                var offset = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80) % BarWidth;
                idBar = new string('─', offset) + '━' + new string('─', BarWidth - offset - 1);
                idBarColor = "blue"; idCheck = $" [blue]{spinnerFrame}[/]";
                idCounts = string.Empty;
            }
            else
            {
                idBar = new string('─', BarWidth); idBarColor = "grey"; idCheck = string.Empty; idCounts = string.Empty;
            }
            identitiesRow = new Markup($"[bold]Identities[/]{idCheck}  [{idBarColor}]{Markup.Escape(idBar)}[/]{idCounts}");
        }

        // ── Row 7: nodes ──────────────────────────────────────────────────────────────
        IRenderable nodesRow;
        {
            string ndBar; string ndBarColor; string ndCheck; string ndCounts;
            if (nodes is not null)
            {
                ndBar = new string('━', BarWidth); ndBarColor = "green"; ndCheck = " [green]✓[/]";
                ndCounts = (nodes.Exported > 0 ? $"  [bold]{nodes.Exported:N0}[/][grey] captured[/]" : string.Empty)
                    + (nodes.AreaPathsReplicated > 0 ? $"  [bold]{nodes.AreaPathsReplicated:N0}[/][grey] area[/]" : string.Empty)
                    + (nodes.IterationPathsReplicated > 0 ? $"  [bold]{nodes.IterationPathsReplicated:N0}[/][grey] iteration[/]" : string.Empty)
                    + (nodes.Failed > 0 ? $"  [red]{nodes.Failed:N0} failed[/]" : string.Empty);
            }
            else if (nodesCount > 0)
            {
                var offset = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80) % BarWidth;
                ndBar = new string('─', offset) + '━' + new string('─', BarWidth - offset - 1);
                ndBarColor = "blue"; ndCheck = $" [blue]{spinnerFrame}[/]";
                ndCounts = string.Empty;
            }
            else
            {
                ndBar = new string('─', BarWidth); ndBarColor = "grey"; ndCheck = string.Empty; ndCounts = string.Empty;
            }
            nodesRow = new Markup($"[bold]Nodes[/]{ndCheck}  [{ndBarColor}]{Markup.Escape(ndBar)}[/]{ndCounts}");
        }

        // ── Row 8: teams ──────────────────────────────────────────────────────────────
        IRenderable teamsRow;
        {
            string tmBar; string tmBarColor; string tmCheck; string tmCounts;
            if (teams is not null)
            {
                tmBar = new string('━', BarWidth); tmBarColor = "green"; tmCheck = " [green]✓[/]";
                tmCounts = $"  [bold]{teams.Exported:N0}[/][grey] exported[/]"
                    + (teams.Imported > 0 ? $"  [bold]{teams.Imported:N0}[/][grey] imported[/]" : string.Empty)
                    + (teams.Members > 0 ? $"  [grey]{teams.Members:N0} members[/]" : string.Empty)
                    + (teams.Failed > 0 ? $"  [red]{teams.Failed:N0} failed[/]" : string.Empty);
            }
            else if (teamsCount > 0)
            {
                var offset = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80) % BarWidth;
                tmBar = new string('─', offset) + '━' + new string('─', BarWidth - offset - 1);
                tmBarColor = "blue"; tmCheck = $" [blue]{spinnerFrame}[/]";
                tmCounts = $"  [grey]{teamsCount:N0} so far[/]";
            }
            else
            {
                tmBar = new string('─', BarWidth); tmBarColor = "grey"; tmCheck = string.Empty; tmCounts = string.Empty;
            }
            teamsRow = new Markup($"[bold]Teams[/]{tmCheck}  [{tmBarColor}]{Markup.Escape(tmBar)}[/]{tmCounts}");
        }

        return new Rows(nodesRow, teamsRow, identitiesRow, wiRow, revRow, timingRow, attachmentRow, checkpointRow);
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
