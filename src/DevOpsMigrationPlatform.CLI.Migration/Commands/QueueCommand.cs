// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.CLI.Views;
using DevOpsMigrationPlatform.Infrastructure.Config;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Diagnostics;
using Microsoft.Extensions.Configuration;
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
        ConfigureDetailedDiagnostics(settings);

        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddHttpClient<ControlPlaneClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<EnvironmentOptions>>().Value;
                client.BaseAddress = new Uri(opts.ControlPlane.BaseUrl);
            });

            services.AddTransient<IJobSubmissionClient>(sp => sp.GetRequiredService<ControlPlaneClient>());

            services.AddSingleton<IProgressSink, AnsiProgressSink>();

            services.AddSingleton<IOptions<JsonSchemaConfigValidatorOptions>>(sp =>
                MsOptions.Create(new JsonSchemaConfigValidatorOptions
                {
                    SchemaPath = Path.Combine(AppContext.BaseDirectory, "migration.schema.json")
                }));
            services.AddSingleton<IConfigSchemaValidator, JsonSchemaConfigValidator>();
        });

        if (settings.Diagnostics)
        {
            var console = GetRequiredService<IAnsiConsole>();
            var diagnosticsPath = ResolveDiagnosticsPath();
            if (!string.IsNullOrWhiteSpace(diagnosticsPath))
                ShowInfo(console, $"DiagnosticsPath: {FormatLocalPathMarkup(diagnosticsPath)}");
        }

        var configPath = GetConfigurationPath(settings);
        var rawJson = configPath != null
            ? await File.ReadAllTextAsync(Path.GetFullPath(configPath), cancellationToken)
            : null;

        if (rawJson is not null)
        {
            var schemaValidator = GetRequiredService<IConfigSchemaValidator>();
            var logger = GetRequiredService<ILogger<QueueCommand>>();
            var console = GetRequiredService<IAnsiConsole>();

            try
            {
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
                    return 1;
                }
            }
            catch (FileNotFoundException ex) when (ex.Message.Contains("migration.schema.json"))
            {
                var expectedSchemaPath = Path.Combine(AppContext.BaseDirectory, "migration.schema.json");
                logger.LogWarning(
                    "Schema file not found at {ExpectedSchemaPath}. Validation skipped.",
                    expectedSchemaPath);
                console.MarkupLine($"[yellow]⚠[/] Schema file not found at {FormatLocalPathMarkup(expectedSchemaPath)}. Validation skipped.");
            }
        }

        if (rawJson is null)
        {
            ShowError(GetRequiredService<IAnsiConsole>(), "No configuration file specified.");
            return 1;
        }

        var mode = ExtractMode(rawJson);
        return mode switch
        {
            "Export" => await ExecuteExportAsync(rawJson, settings, cancellationToken),
            "Prepare" => await ExecutePrepareAsync(rawJson, settings, cancellationToken),
            "Import" => await ExecuteImportAsync(rawJson, settings, cancellationToken),
            "Migrate" => await ExecuteMigrateAsync(rawJson, settings, cancellationToken),
            "Inventory" => await ExecuteDiscoveryJobAsync(JobKind.Inventory, rawJson, settings, cancellationToken),
            "Dependencies" => await ExecuteDiscoveryJobAsync(JobKind.Dependencies, rawJson, settings, cancellationToken),
            _ => ExecuteInvalidMode(mode ?? "(none)")
        };
    }

    private static void ConfigureDetailedDiagnostics(QueueCommandSettings settings)
    {
        if (!settings.Diagnostics)
            return;

        Environment.SetEnvironmentVariable("Telemetry__DetailedDiagnostics", "true");

        var diagnosticsPath = Environment.GetEnvironmentVariable("Telemetry__DiagnosticsPath");
        if (string.IsNullOrWhiteSpace(diagnosticsPath))
        {
            diagnosticsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".otel-diagnostics"));
            Environment.SetEnvironmentVariable("Telemetry__DiagnosticsPath", diagnosticsPath);
        }
    }

    private string? ResolveDiagnosticsPath()
    {
        var configuration = GetRequiredService<IConfiguration>();
        return FileDiagnosticsExtensions.GetDiagnosticsPath(configuration);
    }


    private async Task<int> ExecuteImportAsync(string rawJson, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        using var doc = JsonDocument.Parse(rawJson);
        var mp = doc.RootElement.GetProperty("MigrationPlatform");

        var targetType = GetJsonString(mp, "Target", "Type") ?? string.Empty;
        var isSimulated = string.Equals(targetType, "Simulated", StringComparison.Ordinal);
        var orgUrl = GetJsonString(mp, "Target", "Url") ?? (isSimulated ? "https://simulated.example.com" : null);
        var project = GetJsonString(mp, "Target", "Project") ?? (isSimulated ? "SimulatedProject" : null);
        var packagePath = GetJsonString(mp, "Package", "WorkingDirectory") ?? string.Empty;
        var createPackage = GetJsonBool(mp, false, "Package", "CreatePackage");

        if (string.IsNullOrWhiteSpace(orgUrl))
        {
            ShowError(console, "Target.Url is required for import.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            ShowError(console, "Target.Project is required for import.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(packagePath))
        {
            ShowError(console, "Package.WorkingDirectory is required for import.");
            return 1;
        }

        var outputPath = Path.Combine(Path.GetFullPath(ExpandPath(packagePath)), PackagePathResolver.ExtractOrgFolderName(orgUrl), project);
        console.MarkupLine(isSimulated
            ? "[blue]ℹ[/] Importing into [bold]Simulated[/] target"
            : $"[blue]ℹ[/] Importing into [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path   : [blue]{Markup.Escape(outputPath)}[/]");

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Import,
            ConfigPayload = rawJson,
            Connectors = GetConnectors(mp),
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = createPackage
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
        {
            ShowSuccess(console, "Work item import complete.");
        }

        return 0;
    }


    private int ExecuteImportStub()
    {
        AnsiConsole.MarkupLine("[grey]import — not available in this release.[/]");
        return 1;
    }

    private async Task<int> ExecuteMigrateAsync(string rawJson, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var exportResult = await ExecuteExportAsync(rawJson, settings, cancellationToken);
        if (exportResult != 0)
            return exportResult;

        var prepareResult = await ExecutePrepareAsync(rawJson, settings, cancellationToken);
        if (prepareResult != 0)
            return prepareResult;

        return await ExecuteImportAsync(rawJson, settings, cancellationToken);
    }


    private async Task<int> ExecutePrepareAsync(string rawJson, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        using var doc = JsonDocument.Parse(rawJson);
        var mp = doc.RootElement.GetProperty("MigrationPlatform");

        var packagePath = GetJsonString(mp, "Package", "WorkingDirectory") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            ShowError(console, "Package.WorkingDirectory is required for prepare.");
            return 1;
        }

        var createPackage = GetJsonBool(mp, false, "Package", "CreatePackage");
        var outputPath = Path.GetFullPath(ExpandPath(packagePath));
        console.MarkupLine($"[blue]ℹ[/] Preparing package at [blue]{Markup.Escape(outputPath)}[/]");

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Prepare,
            ConfigPayload = rawJson,
            Connectors = GetConnectors(mp),
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = createPackage
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


    private static string? ExtractMode(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("MigrationPlatform", out var mp) &&
                mp.TryGetProperty("Mode", out var mode))
                return mode.GetString();
        }
        catch
        {
        }

        return null;
    }

    private static string? GetJsonString(JsonElement root, params string[] path)
    {
        var el = root;
        foreach (var key in path)
        {
            if (!el.TryGetProperty(key, out el))
                return null;
        }

        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static bool GetJsonBool(JsonElement root, bool defaultValue, params string[] path)
    {
        var el = root;
        foreach (var key in path)
        {
            if (!el.TryGetProperty(key, out el))
                return defaultValue;
        }

        return el.ValueKind == JsonValueKind.True;
    }

    private static string ExpandPath(string path) =>
        Environment.ExpandEnvironmentVariables(path);

    private static ConnectorType[] GetConnectors(JsonElement mp)
    {
        var connectors = new HashSet<ConnectorType>();

        void AddForType(string? type)
        {
            if (string.Equals(type, "TeamFoundationServer", StringComparison.OrdinalIgnoreCase))
                connectors.Add(ConnectorType.TeamFoundationServer);
            else if (string.Equals(type, "AzureDevOpsServices", StringComparison.OrdinalIgnoreCase))
                connectors.Add(ConnectorType.AzureDevOps);
        }

        AddForType(GetJsonString(mp, "Source", "Type"));
        AddForType(GetJsonString(mp, "Target", "Type"));
        return connectors.Count > 0 ? connectors.ToArray() : Array.Empty<ConnectorType>();
    }

    private static ConnectorType[] GetDiscoveryConnectors(JsonElement mp)
    {
        var connectors = new HashSet<ConnectorType>();
        if (!mp.TryGetProperty("Organisations", out var orgs) || orgs.ValueKind != JsonValueKind.Array)
            return Array.Empty<ConnectorType>();

        foreach (var org in orgs.EnumerateArray())
        {
            if (org.TryGetProperty("Enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
                continue;

            var type = GetJsonString(org, "Type");
            if (string.Equals(type, "TeamFoundationServer", StringComparison.OrdinalIgnoreCase))
                connectors.Add(ConnectorType.TeamFoundationServer);
            else if (string.Equals(type, "AzureDevOpsServices", StringComparison.OrdinalIgnoreCase))
                connectors.Add(ConnectorType.AzureDevOps);
        }

        return connectors.Count > 0 ? connectors.ToArray() : Array.Empty<ConnectorType>();
    }


    private int ExecuteInvalidMode(string mode)
    {
        AnsiConsole.MarkupLine($"[red]Invalid mode '{Markup.Escape(mode)}'. Must be Export, Prepare, Import, Migrate, Inventory, or Dependencies.[/]");
        return 1;
    }


    private async Task<int> ExecuteExportAsync(string rawJson, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var mp = doc.RootElement.GetProperty("MigrationPlatform");
        var sourceType = GetJsonString(mp, "Source", "Type") ?? string.Empty;

        if (string.Equals(sourceType, "Simulated", StringComparison.Ordinal))
            return await ExecuteSimulatedExportAsync(rawJson, settings, cancellationToken);

        return await ExecuteAdoExportAsync(rawJson, settings, cancellationToken);
    }


    private async Task<int> ExecuteSimulatedExportAsync(string rawJson, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        using var doc = JsonDocument.Parse(rawJson);
        var mp = doc.RootElement.GetProperty("MigrationPlatform");

        var orgUrl = GetJsonString(mp, "Source", "Url") ?? "https://simulated.example.com";
        var project = GetJsonString(mp, "Source", "Project") ?? "SimulatedProject";
        var packagePath = GetJsonString(mp, "Package", "WorkingDirectory") ?? string.Empty;
        var createPackage = GetJsonBool(mp, false, "Package", "CreatePackage");

        var outputPath = Path.Combine(
            Path.GetFullPath(ExpandPath(packagePath)),
            PackagePathResolver.ExtractOrgFolderName(orgUrl),
            project);

        console.MarkupLine("[blue]ℹ[/] Exporting from [bold]Simulated[/] source");
        console.MarkupLine($"[blue]ℹ[/] Package path  : [blue]{Markup.Escape(outputPath)}[/]");

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Export,
            ConfigPayload = rawJson,
            Connectors = Array.Empty<ConnectorType>(),
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = createPackage
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
        {
            ShowSuccess(console, "Simulated export complete.");
        }

        return 0;
    }


    private async Task<int> ExecuteAdoExportAsync(string rawJson, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        using var doc = JsonDocument.Parse(rawJson);
        var mp = doc.RootElement.GetProperty("MigrationPlatform");

        var orgUrl = GetJsonString(mp, "Source", "Url");
        var project = GetJsonString(mp, "Source", "Project");
        var packagePath = GetJsonString(mp, "Package", "WorkingDirectory") ?? string.Empty;
        var createPackage = GetJsonBool(mp, false, "Package", "CreatePackage");

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
            Path.GetFullPath(ExpandPath(packagePath)),
            PackagePathResolver.ExtractOrgFolderName(orgUrl),
            project);

        console.MarkupLine($"[blue]ℹ[/] Exporting from [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path  : [blue]{Markup.Escape(outputPath)}[/]");

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Export,
            ConfigPayload = rawJson,
            Connectors = GetConnectors(mp),
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = createPackage
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

        var parsedJobId = Guid.Parse(job.JobId);
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
            parsedJobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, parsedJobId, GetControlPlaneUrl());
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to submit job: {ex.Message}");
            return 1;
        }

        var diagnosticsBuffer = new System.Collections.Concurrent.ConcurrentQueue<string>();

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
            }
            catch (Exception)
            {
            }
        }, followCts.Token);

        var updates = Channel.CreateUnbounded<JobProgressUpdate>();
        var state = JobProgressState.Initial(0);

        var bootstrapTrigger = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bootstrapTask = Task.Run(() => FetchBootstrapOnReady(client, parsedJobId, updates.Writer, bootstrapTrigger, followCts.Token));

        var telemetryTask = Task.Run(() => FetchLatestMetrics(client, parsedJobId, updates.Writer, followCts.Token));
        var progressTask = Task.Run(() => FollowJobProgress(client, parsedJobId, updates.Writer, bootstrapTrigger, followCts.Token));

        if (Console.IsOutputRedirected)
        {
            try
            {
                await foreach (var update in updates.Reader.ReadAllAsync(followCts.Token).ConfigureAwait(false))
                {
                    state = Apply(state, update);
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
                        ctx.Refresh();

                        await foreach (var update in updates.Reader.ReadAllAsync(followCts.Token).ConfigureAwait(false))
                        {
                            state = Apply(state, update);

                            // While Tasks is null the display shows static "Initialising" text.
                            // Every ctx.UpdateTarget call re-emits that line to terminals that
                            // can't fully honour ANSI cursor-up, producing one appended
                            // "Initialising" line per incoming SSE event (one per task).
                            // The initial render from console.Live(...) already shows the text,
                            // so skip all UpdateTarget calls until the task list has arrived.
                            if (state.Tasks is not null)
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
                jobFailed = true;
                ShowError(console, ex.Message);
                ShowError(console, $"Last progress: {state.Completed} exported / {state.Skipped} skipped / {state.Revisions} revisions");
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
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
                Console.CancelKeyPress -= ctrlCHandler;
                followCtsDisposed = true;

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

        await followCts.CancelAsync();
        try { await diagnosticsTask; } catch (OperationCanceledException) { }
        try { await telemetryTask; } catch (OperationCanceledException) { }
        try { await progressTask; } catch (OperationCanceledException) { }
        try { await bootstrapTask; } catch (OperationCanceledException) { }

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



    private async Task<int> ExecuteDiscoveryJobAsync(JobKind kind, string rawJson, QueueCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();
        using var doc = JsonDocument.Parse(rawJson);
        var mp = doc.RootElement.GetProperty("MigrationPlatform");

        var packagePath = GetJsonString(mp, "Package", "WorkingDirectory") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            ShowError(console, "Package.WorkingDirectory is required. Set it in the config file.");
            return 1;
        }

        var outputPath = Path.GetFullPath(ExpandPath(packagePath));
        var packageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}";

        var orgCount = 0;
        if (mp.TryGetProperty("Organisations", out var orgsEl) && orgsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var org in orgsEl.EnumerateArray())
            {
                if (!org.TryGetProperty("Enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False)
                    orgCount++;
            }
        }

        console.MarkupLine($"[blue]ℹ[/] Submitting {kind} job for [bold]{orgCount}[/] organisation(s).");
        console.MarkupLine($"[blue]ℹ[/] Output path: [blue]{Markup.Escape(outputPath)}[/]");

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            ConfigVersion = "2.0",
            Kind = kind,
            ConfigPayload = rawJson,
            Connectors = GetDiscoveryConnectors(mp),
            Package = new JobPackage { PackageUri = packageUri },
            Diagnostics = new JobDiagnostics { MinimumLevel = settings.Level },
            Resume = settings.ForceFresh ? new JobResume { Mode = ResumeMode.ForceFresh } : null
        };

        var envOpts = GetRequiredService<IOptions<EnvironmentOptions>>().Value;
        var isStandalone = envOpts.Type == EnvironmentType.Standalone;
        var client = GetRequiredService<ControlPlaneClient>();

        Guid jobId;
        try
        {
            jobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, jobId, GetControlPlaneUrl());
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to submit job: {ex.Message}");
            return 1;
        }

        if (!isStandalone && !settings.Follow)
        {
            console.MarkupLine($"[grey]Use [blue]manage status --job {jobId}[/] to check progress.[/]");
            return 0;
        }

        using var followCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var followCtsDisposed = false;
        ConsoleCancelEventHandler ctrlCHandler = (_, e) =>
        {
            e.Cancel = true;
            if (!followCtsDisposed)
                followCts.Cancel();
        };
        Console.CancelKeyPress += ctrlCHandler;
        var jobFailed = false;

        var updates = Channel.CreateUnbounded<JobProgressUpdate>();
        var discoveryState = DiscoveryProgressState.Initial();

        var bootstrapTrigger = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bootstrapTask = Task.Run(() => FetchBootstrapOnReady(client, jobId, updates.Writer, bootstrapTrigger, followCts.Token));
        var progressTask = Task.Run(() => FollowJobProgress(client, jobId, updates.Writer, bootstrapTrigger, followCts.Token));
        // Ticker: emits a TimerTick every 120 ms so the live spinner animates even when the
        // agent is silent (e.g. during long WIQL queries between heartbeats).
        var tickerTask = Task.Run(async () =>
        {
            try
            {
                while (!followCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(120, followCts.Token).ConfigureAwait(false);
                    if (!updates.Writer.TryWrite(new TimerTick()))
                        break; // channel was completed by FollowJobProgress
                }
            }
            catch (OperationCanceledException) { }
        });

        if (Console.IsOutputRedirected)
        {
            try
            {
                await foreach (var update in updates.Reader.ReadAllAsync(followCts.Token).ConfigureAwait(false))
                {
                    discoveryState = ApplyDiscovery(discoveryState, update);

                    if (update is TaskListReceived tlr && tlr.Tasks.Tasks.Count > 0)
                    {
                        console.MarkupLine("[grey]Execution plan:[/]");
                        foreach (var t in tlr.Tasks.Tasks.OrderBy(t => t.Order))
                            console.MarkupLine($"[grey]  · {Markup.Escape(t.Name)}[/]");
                    }

                    if (update is StageAdvanced sa)
                    {
                        var stage = sa.Event.Stage ?? string.Empty;
                        var message = sa.Event.Message ?? string.Empty;
                        if ((stage == "Inventory" || stage == "ProjectComplete" || stage == "Failed") && message.Contains('|'))
                        {
                            var sepIdx = message.IndexOf('|');
                            var projectName = message[(sepIdx + 1)..];
                            var wi = sa.Event.Metrics?.Scope?.WorkItemsTotal ?? 0;
                            var links = sa.Event.Metrics?.Discovery?.Dependencies?.ExternalLinksFound ?? 0;
                            var statusIcon = stage == "Failed" ? "✗" : "✓";
                            var statusColor = stage == "Failed" ? "red" : "green";
                            var summary = stage == "ProjectComplete"
                                ? $"[grey]{wi:N0} work items, {links:N0} links[/]"
                                : $"[grey]{wi:N0} work items[/]";
                            console.MarkupLine($"[{statusColor}]{statusIcon}[/] {Markup.Escape(projectName)}  {summary}");
                        }
                        else if ((stage == "Progress" || stage == "Analysis" || stage == "Counting") && message.Contains('|'))
                        {
                            var sepIdx = message.IndexOf('|');
                            var projectName = message[(sepIdx + 1)..];
                            var wi = sa.Event.Metrics?.Scope?.WorkItemsTotal ?? 0;
                            var label = stage == "Counting" ? "counting" : "analysing";
                            if (wi > 0)
                                console.MarkupLine($"[blue]⠋[/] {Markup.Escape(projectName)}  [grey]{wi:N0} {label}…[/]");
                        }
                    }

                    if (update is JobTerminated jt)
                    {
                        // On fast jobs the SSE stream ends before the bootstrap HTTP call
                        // completes, so TaskListReceived arrives after this break. Drain it
                        // here so the final summary (if any) is accurate.
                        try { await bootstrapTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                        catch (Exception) { /* best-effort */ }
                        while (updates.Reader.TryRead(out var pending))
                            discoveryState = ApplyDiscovery(discoveryState, pending);

                        if (jt.Failed)
                        {
                            jobFailed = true;
                            ShowError(console, jt.Reason ?? "Job failed");
                        }
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= ctrlCHandler;
                followCtsDisposed = true;
            }
        }
        else
        {
            var cancelled = false;
            var logBuffer = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(logBuffer);

            try
            {
                await console.Live(BuildDiscoveryDisplay(discoveryState))
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .StartAsync(async ctx =>
                    {
                        ctx.Refresh();
                        await foreach (var update in updates.Reader.ReadAllAsync(followCts.Token).ConfigureAwait(false))
                        {
                            discoveryState = ApplyDiscovery(discoveryState, update);

                            // While Tasks is null the display shows static "Initialising" text.
                            // Every ctx.UpdateTarget call re-emits that line to terminals that
                            // can't fully honour ANSI cursor-up, producing one appended
                            // "Initialising" line per incoming SSE event (one per task).
                            // The initial render from console.Live(...) already shows the text,
                            // so skip all UpdateTarget calls until the task list has arrived.
                            if (discoveryState.Tasks is not null || discoveryState.Projects.Count > 0)
                                ctx.UpdateTarget(BuildDiscoveryDisplay(discoveryState));

                            if (update is JobTerminated jt)
                            {
                                // On fast jobs the SSE stream ends before the bootstrap HTTP
                                // call completes, leaving the display stuck at "Initialising".
                                // Await the bootstrap task then drain any pending updates so
                                // the final render shows tasks rather than the spinner text.
                                try { await bootstrapTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                                catch (OperationCanceledException) { }
                                catch (Exception) { /* best-effort */ }
                                while (updates.Reader.TryRead(out var pending))
                                    discoveryState = ApplyDiscovery(discoveryState, pending);
                                ctx.UpdateTarget(BuildDiscoveryDisplay(discoveryState));

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
                jobFailed = true;
                ShowError(console, ex.Message);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                if (isStandalone)
                    console.MarkupLine("[yellow]Cancelled. Job has been stopped.[/]");
                else
                {
                    console.MarkupLine("[yellow]Detached from stream. Job continues running.[/]");
                    console.MarkupLine("[grey]Use [bold]tui[/] to resume watching.[/]");
                }
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= ctrlCHandler;
                followCtsDisposed = true;
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

        await followCts.CancelAsync();
        try { await progressTask; } catch (OperationCanceledException) { }
        try { await bootstrapTask; } catch (OperationCanceledException) { }

        if (jobFailed) return 1;

        if (discoveryState.Projects.Count > 0)
        {
            var completedProjects = discoveryState.Projects.Count(p => p.IsComplete);
            var totalWi = discoveryState.Projects.Sum(p => p.WorkItems);
            var totalRev = discoveryState.Projects.Sum(p => p.Revisions);
            ShowSuccess(console, $"{kind} complete — {completedProjects} project(s)  ·  {totalWi:N0} work items  ·  {totalRev:N0} revisions.");
        }
        else
        {
            ShowSuccess(console, $"{kind} complete.");
        }

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
    /// <summary>
    /// Fired when the bootstrap poll returns a snapshot with org/project data.
    /// Used to pre-populate the discovery table for resuming jobs where SSE catchup
    /// events may no longer be in the ring buffer.
    /// </summary>
    private record SnapshotLoaded(JobSnapshot Snapshot) : JobProgressUpdate;
    /// <summary>No-op tick emitted every ~120 ms to keep the live spinner animating.</summary>
    private record TimerTick : JobProgressUpdate;

    private static JobProgressState Apply(JobProgressState state, JobProgressUpdate update) => update switch
    {
        TelemetryPolled t => ApplyTelemetry(state, t.Metrics),
        StageAdvanced t => ApplyStageAdvance(state, t.Event),
        TaskListReceived t => ApplyTaskListReceived(state, t.Tasks),
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
            Completed = wi is not null ? Math.Max(state.Completed, (int)wi.Completed) : state.Completed,
            Skipped = wi is not null ? Math.Max(state.Skipped, (int)wi.Skipped) : state.Skipped,
            Revisions = wi is not null && wi.RevisionsProcessed > 0
                ? Math.Max(state.Revisions, (int)wi.RevisionsProcessed)
                : state.Revisions,
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

    private static JobProgressState ApplyTaskListReceived(JobProgressState state, JobTaskList taskList)
    {
        // Seed TotalWorkItems from the plan's KnownTotal so the bar renders
        // correctly before the first telemetry poll arrives.
        var wiTask = taskList.Tasks.FirstOrDefault(t =>
            t.Id.Contains("workitems", StringComparison.OrdinalIgnoreCase));
        var knownTotal = (int)(wiTask?.KnownTotal ?? 0);
        return state with
        {
            Tasks = taskList,
            TotalWorkItems = knownTotal > 0 ? knownTotal : state.TotalWorkItems,
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
                    DependencyCapture = inMig?.DependencyCapture ?? exMig?.DependencyCapture,
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
        // When the task list has arrived, render a single unified view where every
        // task row IS its module progress bar. No separate checklist + divider.
        if (s.Tasks is { Tasks.Count: > 0 })
        {
            var phases = GetOrderedTaskPhases(s.Tasks.Tasks);
            if (phases.Count > 1)
                return BuildMultiStageTaskDisplay(s, phases);

            return BuildUnifiedTaskDisplay(s);
        }

        // Initialising: task list not yet received.
        if (s.Tasks is null)
            return new Markup("[grey]⠋ Initialising — waiting for agent to start…[/]");

        // Tasks received but empty (shouldn't happen in practice) — show legacy detail.
        return BuildProgressRenderable(
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
            s.TeamsCount, s.NodesCount, s.IdentitiesCount,
                s.Metrics?.Migration?.DependencyCapture ?? s.Metrics?.Discovery?.Dependencies);
    }

    /// <summary>
    /// Single unified display: each task row IS its module progress bar.
    /// Identities/Nodes/Teams show their completion bar inline.
    /// WorkItems shows the full bar + sub-rows (current WI, timing, attachments, checkpoint).
    /// </summary>
    private static IRenderable BuildMultiStageTaskDisplay(JobProgressState s, IReadOnlyList<string> phases)
    {
        var currentPhase = DetermineCurrentTaskPhase(s, phases);
        var rows = new System.Collections.Generic.List<IRenderable>
        {
            new Markup(BuildTaskStageStrip(phases, currentPhase)),
            new Markup(string.Empty),
            BuildUnifiedTaskDisplay(s, currentPhase, showPhaseHeaders: false)
        };

        return new Rows(rows);
    }

    private static IRenderable BuildUnifiedTaskDisplay(
        JobProgressState s,
        string? phaseFilter = null,
        bool showPhaseHeaders = true)
    {
        const int BarWidth = 38;
        var spinnerFrame = s_spinnerFrames[(int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80 % s_spinnerFrames.Length)];
        var rows = new System.Collections.Generic.List<IRenderable>();
        string? currentPhase = null;
        var estimatedTotalRevisions = s.Completed > 0
            ? (int)((double)s.Revisions / s.Completed * s.TotalWorkItems)
            : 0;

        var tasksToRender = s.Tasks!.Tasks
            .Where(t => ShouldRenderTaskInPhase(t, phaseFilter))
            .OrderBy(t => t.Order)
            .ToList();

        foreach (var task in tasksToRender)
        {
            if (showPhaseHeaders && task.Phase != currentPhase)
            {
                currentPhase = task.Phase;
                if (!string.IsNullOrEmpty(currentPhase))
                    rows.Add(new Markup($"[bold grey]{Markup.Escape(currentPhase)}[/]"));
            }

            var (icon, iconColor) = task.Status switch
            {
                JobTaskStatus.Running => (spinnerFrame, "blue"),
                JobTaskStatus.Completed => ("✓", "green"),
                JobTaskStatus.Failed => ("✗", "red"),
                JobTaskStatus.Skipped => ("→", "grey"),
                _ => ("·", "grey"),
            };

            var taskLower = task.Id.ToLowerInvariant();

            if (taskLower.Contains("identities"))
            {
                var id = s.Metrics?.Migration?.Identities;
                string bar; string barColor; string counts;
                if (id != null || task.Status == JobTaskStatus.Completed)
                {
                    bar = new string('━', BarWidth); barColor = iconColor;
                    counts = id != null
                        ? $"  [bold]{id.Exported:N0}[/][grey] exported[/]"
                            + (id.Resolved > 0 ? $"  [bold]{id.Resolved:N0}[/][grey] resolved[/]" : string.Empty)
                            + (id.Unresolved > 0 ? $"  [yellow]{id.Unresolved:N0} unresolved[/]" : string.Empty)
                            + (id.Failed > 0 ? $"  [red]{id.Failed:N0} failed[/]" : string.Empty)
                        : (s.IdentitiesCount > 0 ? $"  [grey]{s.IdentitiesCount:N0}[/]" : string.Empty);
                }
                else if (task.Status == JobTaskStatus.Running)
                {
                    var offset = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80) % BarWidth;
                    bar = new string('─', offset) + '━' + new string('─', BarWidth - offset - 1);
                    barColor = "blue"; counts = string.Empty;
                }
                else
                {
                    bar = new string('─', BarWidth); barColor = "grey"; counts = string.Empty;
                }
                rows.Add(new Markup($"  [{iconColor}]{icon}[/]  [bold]{Markup.Escape(task.Name)}[/]  [{barColor}]{Markup.Escape(bar)}[/]{counts}"));
            }
            else if (taskLower.Contains("nodes"))
            {
                var nd = s.Metrics?.Migration?.Nodes;
                string bar; string barColor; string counts;
                if (nd != null || task.Status == JobTaskStatus.Completed)
                {
                    bar = new string('━', BarWidth); barColor = iconColor;
                    counts = nd != null
                        ? (nd.Exported > 0 ? $"  [bold]{nd.Exported:N0}[/][grey] captured[/]" : string.Empty)
                            + (nd.AreaPathsReplicated > 0 ? $"  [bold]{nd.AreaPathsReplicated:N0}[/][grey] area[/]" : string.Empty)
                            + (nd.IterationPathsReplicated > 0 ? $"  [bold]{nd.IterationPathsReplicated:N0}[/][grey] iteration[/]" : string.Empty)
                            + (nd.Failed > 0 ? $"  [red]{nd.Failed:N0} failed[/]" : string.Empty)
                        : (s.NodesCount > 0 ? $"  [grey]{s.NodesCount:N0}[/]" : string.Empty);
                }
                else if (task.Status == JobTaskStatus.Running)
                {
                    var offset = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80) % BarWidth;
                    bar = new string('─', offset) + '━' + new string('─', BarWidth - offset - 1);
                    barColor = "blue"; counts = string.Empty;
                }
                else
                {
                    bar = new string('─', BarWidth); barColor = "grey"; counts = string.Empty;
                }
                rows.Add(new Markup($"  [{iconColor}]{icon}[/]  [bold]{Markup.Escape(task.Name)}[/]  [{barColor}]{Markup.Escape(bar)}[/]{counts}"));
            }
            else if (taskLower.Contains("teams"))
            {
                var tm = s.Metrics?.Migration?.Teams;
                string bar; string barColor; string counts;
                if (tm != null || task.Status == JobTaskStatus.Completed)
                {
                    bar = new string('━', BarWidth); barColor = iconColor;
                    counts = tm != null
                        ? (tm.Exported > 0 ? $"  [bold]{tm.Exported:N0}[/][grey] exported[/]" : string.Empty)
                            + (tm.Skipped > 0 ? $"  [grey]{tm.Skipped:N0} already present[/]" : string.Empty)
                            + (tm.Imported > 0 ? $"  [bold]{tm.Imported:N0}[/][grey] imported[/]" : string.Empty)
                            + (tm.Members > 0 ? $"  [grey]{tm.Members:N0} members[/]" : string.Empty)
                            + (tm.Failed > 0 ? $"  [red]{tm.Failed:N0} failed[/]" : string.Empty)
                            + (tm.Exported == 0 && tm.Skipped == 0 && tm.Imported == 0 ? $"  [grey]0 teams[/]" : string.Empty)
                        : (s.TeamsCount > 0 ? $"  [grey]{s.TeamsCount:N0}[/]" : string.Empty);
                }
                else if (task.Status == JobTaskStatus.Running)
                {
                    var offset = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80) % BarWidth;
                    bar = new string('─', offset) + '━' + new string('─', BarWidth - offset - 1);
                    barColor = "blue"; counts = string.Empty;
                }
                else
                {
                    bar = new string('─', BarWidth); barColor = "grey"; counts = string.Empty;
                }
                rows.Add(new Markup($"  [{iconColor}]{icon}[/]  [bold]{Markup.Escape(task.Name)}[/]  [{barColor}]{Markup.Escape(bar)}[/]{counts}"));
            }
            else if (taskLower.Contains("workitems"))
            {
                // WorkItems: full bar + detailed sub-rows
                int processed = s.Completed + s.Skipped;
                string wiBar; string wiBarColor; string countsSuffix;
                if (s.TotalWorkItems > 0)
                {
                    var wiPct = (double)processed / s.TotalWorkItems;
                    var wiFilled = Math.Clamp((int)(wiPct * BarWidth), 0, BarWidth);
                    wiBar = new string('━', wiFilled) + new string('─', BarWidth - wiFilled);
                    wiBarColor = task.Status == JobTaskStatus.Completed ? "green"
                                : task.Status == JobTaskStatus.Running ? "blue" : "grey";
                    var exportedStr = s.Completed > 0 ? $"  [green]{s.Completed:N0} exported[/]" : string.Empty;
                    var skippedStr = s.Skipped > 0 ? $"  [grey]{s.Skipped:N0} skipped[/]" : string.Empty;
                    var etaStr = ComputeRevisionEta(s.Revisions, estimatedTotalRevisions, s.AverageRevDurationMs);
                    countsSuffix = $"  [bold]{processed:N0}[/][grey]/{s.TotalWorkItems:N0}[/]{exportedStr}{skippedStr}"
                                 + $"  [grey]{wiPct * 100.0:F1}%[/]  [grey]ETA: {Markup.Escape(etaStr)}[/]";
                }
                else if (task.Status == JobTaskStatus.Running)
                {
                    // Total unknown — animated indeterminate scanner bar
                    var offset = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80) % BarWidth;
                    wiBar = new string('─', offset) + '━' + new string('─', BarWidth - offset - 1);
                    wiBarColor = "blue";
                    // Don't say "N processed" — that's just exported+skipped restated and
                    // makes the line too long. Show the breakdown directly like the deterministic branch.
                    var expStr = s.Completed > 0 ? $"  [green]{s.Completed:N0} exported[/]" : string.Empty;
                    var skipStr = s.Skipped > 0 ? $"  [grey]{s.Skipped:N0} skipped[/]" : string.Empty;
                    countsSuffix = expStr.Length > 0 || skipStr.Length > 0
                        ? expStr + skipStr
                        : string.Empty;
                }
                else
                {
                    wiBar = new string('─', BarWidth);
                    wiBarColor = "grey";
                    countsSuffix = string.Empty;
                }
                var stageStr = string.IsNullOrEmpty(s.Stage) ? string.Empty : $"  [grey]{Markup.Escape(s.Stage)}[/]";
                rows.Add(new Markup(
                    $"  [{iconColor}]{icon}[/]  [bold]{Markup.Escape(task.Name)}[/]{stageStr}  [{wiBarColor}]{Markup.Escape(wiBar)}[/]{countsSuffix}"));

                if (s.Revisions > 0 || estimatedTotalRevisions > 0)
                {
                    var revisionTotalText = estimatedTotalRevisions > 0
                        ? $"[bold]{s.Revisions:N0}[/][grey]/{estimatedTotalRevisions:N0} revisions[/]"
                        : $"[bold]{s.Revisions:N0}[/][grey] revisions[/]";
                    rows.Add(new Markup($"     [grey]↳ Overall revisions:[/] {revisionTotalText}"));
                }

                if (task.Status == JobTaskStatus.Running || s.CurrentWiId > 0)
                {
                    var isResuming = string.Equals(s.Stage, "Resuming", StringComparison.OrdinalIgnoreCase);
                    if (isResuming && s.CurrentWiId > 0)
                    {
                        var totalStr = s.TotalWorkItems > 0 ? $"/{s.TotalWorkItems:N0}" : string.Empty;
                        rows.Add(new Markup($"     [grey]↳ Resuming WI {s.CurrentWiId}[/]  [grey](fast-forwarding {s.Skipped:N0}{totalStr})[/]"));
                    }
                    else if (s.CurrentWiId > 0)
                    {
                        int refRevs = s.LastWiRevisions > 0 ? s.LastWiRevisions : s.CurrentWiRevsWritten;
                        var curPct = refRevs > 0 ? Math.Min(1.0, (double)s.CurrentWiRevsWritten / refRevs) : 0.0;
                        var curFilled = Math.Clamp((int)(curPct * BarWidth), 0, BarWidth);
                        var curBar = new string('━', curFilled) + new string('─', BarWidth - curFilled);
                        var lastRevStr = s.LastWiRevisions > 0 ? $"  [grey](prev: {s.LastWiRevisions} rev)[/]" : string.Empty;
                        var statusBadge = s.LastWorkItemStatus == "Exported" ? " [green]✓[/]"
                                        : s.LastWorkItemStatus == "Failed" ? " [red]✗[/]" : string.Empty;
                        rows.Add(new Markup(
                            $"     [grey]↳ WI {s.CurrentWiId}[/]{statusBadge}   [blue]{Markup.Escape(curBar)}[/]"
                            + $"  [bold]{s.CurrentWiRevsWritten:N0}[/][grey] rev[/]{lastRevStr}"));
                    }
                    else
                        rows.Add(new Markup("     [grey]↳ Revisions   waiting…[/]"));

                    if (isResuming)
                        rows.Add(new Markup("     [grey]─ (fast-forwarding, no timing)[/]"));
                    else if (s.LastRevDurationMs > 0)
                    {
                        var lastSec = s.LastRevDurationMs / 1000.0;
                        var avgSec = s.AverageRevDurationMs / 1000.0;
                        var isSlowdown = s.AverageRevDurationMs > 0 && s.LastRevDurationMs > s.AverageRevDurationMs * 3;
                        var durationColor = isSlowdown ? "red" : s.LastRevDurationMs > s.AverageRevDurationMs * 1.5 ? "yellow" : "green";
                        var throttleWarning = isSlowdown ? "  [red bold]⚠ possible back-off[/]" : string.Empty;
                        rows.Add(new Markup($"     [grey]last:[/] [{durationColor}]{lastSec:F1}s[/]  [grey]avg:[/] [grey]{avgSec:F1}s[/]{throttleWarning}"));
                    }
                    else
                        rows.Add(new Markup("     [grey]─[/]"));

                    if (s.AttachmentsProcessed > 0 || s.AttachmentsFailed > 0 || s.CurrentAttachmentName != null)
                    {
                        var avgDurStr = s.AverageAttachmentDurationMs > 0 ? $"  [grey]avg dl:[/] [white]{s.AverageAttachmentDurationMs / 1000.0:F1}s[/]" : string.Empty;
                        var avgSzStr = s.AverageAttachmentSizeBytes > 0 ? $"  [grey]avg size:[/] [white]{FormatBytes(s.AverageAttachmentSizeBytes)}[/]" : string.Empty;
                        var failStr = s.AttachmentsFailed > 0 ? $"  [red]{s.AttachmentsFailed} failed[/]" : string.Empty;
                        var inFlightStr = s.CurrentAttachmentName != null
                            ? $"  [yellow]↓ {Markup.Escape(TruncateName(s.CurrentAttachmentName, 28))}[/]" : string.Empty;
                        rows.Add(new Markup($"     [grey]Attachments:[/] [bold]{s.AttachmentsProcessed:N0}[/][grey] done[/]{failStr}{inFlightStr}{avgDurStr}{avgSzStr}"));
                    }
                    else
                        rows.Add(new Markup("     [grey]Attachments   –[/]"));

                    if (s.NextCheckpointDueAt is null && s.LastCheckpointAt is not null)
                        rows.Add(new Markup("     [green]✓ Safe to cancel — checkpointed per revision[/]"));
                    else if (s.NextCheckpointDueAt is not null)
                    {
                        var remaining = s.NextCheckpointDueAt.Value - DateTimeOffset.UtcNow;
                        rows.Add(remaining <= TimeSpan.Zero
                            ? new Markup("     [green]✓ Save point due now[/]")
                            : new Markup($"     [yellow]⏳ Next save in {(int)remaining.TotalMinutes}m {remaining.Seconds:D2}s[/]"));
                    }
                    else
                        rows.Add(new Markup("     [grey]─[/]"));

                    var completedDuration = TryGetTaskElapsed(task);
                    if (task.Status == JobTaskStatus.Completed && completedDuration.HasValue)
                        rows.Add(new Markup($"     [grey]duration:[/] [white]{Markup.Escape(FormatElapsed(completedDuration.Value))}[/]"));
                }
                else if (task.Status == JobTaskStatus.Completed)
                {
                    var completedDuration = TryGetTaskElapsed(task);
                    if (completedDuration.HasValue)
                        rows.Add(new Markup($"     [grey]duration:[/] [white]{Markup.Escape(FormatElapsed(completedDuration.Value))}[/]"));
                }
            }
            else
            {
                // Generic task — show name + optional progress from KnownTotal
                string suffix = string.Empty;
                if (task.KnownTotal.HasValue && task.KnownTotal > 0)
                {
                    var done = task.CompletedCount ?? 0;
                    var pct = Math.Clamp((double)done / task.KnownTotal.Value, 0, 1);
                    var filled = (int)(pct * BarWidth);
                    var bar = new string('━', filled) + new string('─', BarWidth - filled);
                    suffix = $"  [{iconColor}]{Markup.Escape(bar)}[/]  [grey]{done:N0}/{task.KnownTotal.Value:N0}[/]";
                }
                else if (task.CompletedCount > 0)
                    suffix = $"  [grey]{task.CompletedCount.Value:N0}[/]";
                rows.Add(new Markup($"  [{iconColor}]{icon}[/]  {Markup.Escape(task.Name)}{suffix}"));
            }
        }

        var remainingTasks = s.Tasks.Tasks.Count(task => !IsTerminal(task.Status));
        var overallEta = ComputeOverallTaskEta(s, estimatedTotalRevisions);
        rows.Add(new Markup("[grey]────────────────────────────────────────────────────────────────────────[/]"));
        rows.Add(new Markup(
            overallEta.HasValue
                ? $"[bold]Remaining tasks:[/] {remainingTasks}  [bold]Overall ETA:[/] [white]{Markup.Escape(FormatEta(overallEta.Value))}[/]"
                : $"[bold]Remaining tasks:[/] {remainingTasks}  [bold]Overall ETA:[/] [grey]unknown[/]"));

        return rows.Count > 0 ? new Rows(rows) : new Markup("[grey]⠋ Running…[/]");
    }

    private static IReadOnlyList<string> GetOrderedTaskPhases(IReadOnlyList<JobTask> tasks) => tasks
        .Select(t => t.Phase)
        .Where(phase => !string.IsNullOrWhiteSpace(phase))
        .Select(phase => phase!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string DetermineCurrentTaskPhase(JobProgressState s, IReadOnlyList<string> phases)
    {
        var runningPhase = s.Tasks!.Tasks
            .Where(t => t.Status == JobTaskStatus.Running && !string.IsNullOrWhiteSpace(t.Phase))
            .Select(t => t.Phase!)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(runningPhase))
            return runningPhase;

        var pendingPhase = s.Tasks.Tasks
            .Where(t => t.Status == JobTaskStatus.Pending && !string.IsNullOrWhiteSpace(t.Phase))
            .Select(t => t.Phase!)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(pendingPhase))
            return pendingPhase;

        if (!string.IsNullOrWhiteSpace(s.Stage))
        {
            var match = phases.FirstOrDefault(phase => s.Stage.Contains(phase, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }

        var terminalPhase = s.Tasks.Tasks
            .Where(t => IsTerminal(t.Status) && !string.IsNullOrWhiteSpace(t.Phase))
            .OrderBy(t => t.Order)
            .Select(t => t.Phase!)
            .LastOrDefault();
        if (!string.IsNullOrWhiteSpace(terminalPhase))
            return terminalPhase;

        return phases[^1];
    }

    private static bool ShouldRenderTaskInPhase(JobTask task, string? phaseFilter)
    {
        if (string.IsNullOrWhiteSpace(phaseFilter))
            return true;

        if (string.IsNullOrWhiteSpace(task.Phase))
            return true;

        return string.Equals(task.Phase, phaseFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTaskStageStrip(IReadOnlyList<string> phases, string currentPhase)
    {
        var segments = phases.Select(phase =>
            string.Equals(phase, currentPhase, StringComparison.OrdinalIgnoreCase)
                ? $"[black on white] {Markup.Escape(phase)} [/]"
                : $"[grey]{Markup.Escape(phase)}[/]");

        return string.Join(" [grey]>[/] ", segments);
    }

    /// <summary>
    /// Awaits a <c>Job.Ready</c> signal from the SSE stream (via <paramref name="bootstrapTrigger"/>),
    /// then performs a single <c>GET /jobs/{jobId}/bootstrap</c> call to retrieve the task list and
    /// writes a <see cref="TaskListReceived"/> update. Falls back to polling at 2-second intervals
    /// if the agent does not support lifecycle events (older agent, or plan-build failure).
    /// </summary>
    private static async Task FetchBootstrapOnReady(
        IControlPlaneClient client, Guid jobId,
        ChannelWriter<JobProgressUpdate> updates,
        TaskCompletionSource<long> bootstrapTrigger,
        CancellationToken ct)
    {
        try
        {
            // Wait for Job.Ready signal (or fall back after 60 s for older agents).
            await Task.WhenAny(bootstrapTrigger.Task, Task.Delay(TimeSpan.FromSeconds(60), ct))
                .ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // One-shot bootstrap GET — the task list should be present by now.
            // If not (e.g. plan-build failure), retry at 2 s intervals up to 10 s.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                try
                {
                    var bootstrap = await client.GetBootstrapAsync(jobId, ct).ConfigureAwait(false);
                    if (bootstrap?.Tasks is not null)
                    {
                        if (bootstrap.Metrics is not null)
                        {
                            await updates.WriteAsync(
                                new TelemetryPolled(bootstrap.Metrics), ct)
                                .ConfigureAwait(false);
                        }

                        await updates.WriteAsync(
                            new TaskListReceived(bootstrap.Tasks, bootstrap.LastEventSequence), ct)
                            .ConfigureAwait(false);

                        // Pre-populate the discovery table from the snapshot so that
                        // previously-completed projects appear immediately for late-joining
                        // clients whose SSE ring buffer no longer holds the catchup events.
                        if (bootstrap.Snapshot is { Organisations.Count: > 0 })
                        {
                            await updates.WriteAsync(
                                new SnapshotLoaded(bootstrap.Snapshot), ct)
                                .ConfigureAwait(false);
                        }

                        return;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception) { /* best-effort — retry */ }

                await Task.Delay(2_000, ct).ConfigureAwait(false);
            }

            // Final fallback: signal with empty list so the display exits "Initialising" state.
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
        ChannelWriter<JobProgressUpdate> updates,
        TaskCompletionSource<long> bootstrapTrigger,
        CancellationToken ct)
    {
        try
        {
            await foreach (var evt in client.FollowLogsAsync(jobId, ct, null).ConfigureAwait(false))
            {
                // Job.Ready signals the task list is available — trigger the one-shot bootstrap GET.
                if (evt.Module == "Job" && evt.Stage == "Job.Ready")
                    bootstrapTrigger.TrySetResult(evt.EventSequence);

                await updates.WriteAsync(new StageAdvanced(evt), ct).ConfigureAwait(false);
            }

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
        int teamsCount = 0, int nodesCount = 0, int identitiesCount = 0,
        DependencyCounters? dependencyCapture = null)
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

        // ── Row 0: dependency capture ─────────────────────────────────────────────────
        IRenderable dependenciesRow;
        {
            if (dependencyCapture is not null)
            {
                var depBar = new string('━', BarWidth);
                var depCounts = (dependencyCapture.WorkItemsAnalysed > 0
                        ? $"  [bold]{dependencyCapture.WorkItemsAnalysed:N0}[/][grey] analysed[/]"
                        : string.Empty)
                    + (dependencyCapture.ExternalLinksFound > 0
                        ? $"  [bold]{dependencyCapture.ExternalLinksFound:N0}[/][grey] links[/]"
                        : string.Empty)
                    + (dependencyCapture.CrossProjectLinks > 0
                        ? $"  [grey]{dependencyCapture.CrossProjectLinks:N0} cross-project[/]"
                        : string.Empty);
                dependenciesRow = new Markup($"[bold]Dependencies[/] [green]✓[/]  [green]{Markup.Escape(depBar)}[/]{depCounts}");
            }
            else
            {
                dependenciesRow = new Markup("[bold]Dependencies[/]  [grey]─[/]");
            }
        }

        return new Rows(dependenciesRow, nodesRow, teamsRow, identitiesRow, wiRow, revRow, timingRow, attachmentRow, checkpointRow);
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

    private static bool IsTerminal(JobTaskStatus status) =>
        status is JobTaskStatus.Completed or JobTaskStatus.Failed or JobTaskStatus.Skipped;

    private static TimeSpan? TryGetTaskElapsed(JobTask task)
    {
        if (!task.StartedAt.HasValue || !task.CompletedAt.HasValue)
            return null;

        var elapsed = task.CompletedAt.Value - task.StartedAt.Value;
        return elapsed > TimeSpan.Zero ? elapsed : null;
    }

    private static TimeSpan? ComputeOverallTaskEta(JobProgressState state, int estimatedTotalRevisions)
    {
        if (state.Tasks is null)
            return null;

        var completedDurations = state.Tasks.Tasks
            .Select(TryGetTaskElapsed)
            .Where(duration => duration.HasValue)
            .Select(duration => duration!.Value)
            .ToArray();

        var averageCompletedTaskDuration = completedDurations.Length > 0
            ? TimeSpan.FromMilliseconds(completedDurations.Average(duration => duration.TotalMilliseconds))
            : (TimeSpan?)null;

        TimeSpan totalRemaining = TimeSpan.Zero;
        var hasEstimate = false;

        foreach (var task in state.Tasks.Tasks)
        {
            if (IsTerminal(task.Status))
                continue;

            if (task.Id.Contains("workitems", StringComparison.OrdinalIgnoreCase))
            {
                var workItemsEta = ComputeRemainingRevisionTime(state.Revisions, estimatedTotalRevisions, state.AverageRevDurationMs);
                if (workItemsEta.HasValue)
                {
                    totalRemaining += workItemsEta.Value;
                    hasEstimate = true;
                    continue;
                }
            }

            if (task.Status == JobTaskStatus.Running
                && task.StartedAt.HasValue
                && task.KnownTotal.HasValue
                && task.KnownTotal.Value > 0
                && task.CompletedCount.HasValue
                && task.CompletedCount.Value > 0)
            {
                var elapsed = DateTimeOffset.UtcNow - task.StartedAt.Value;
                var remainingCount = task.KnownTotal.Value - task.CompletedCount.Value;
                if (remainingCount > 0)
                {
                    totalRemaining += TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / task.CompletedCount.Value * remainingCount);
                    hasEstimate = true;
                    continue;
                }
            }

            if (averageCompletedTaskDuration.HasValue)
            {
                totalRemaining += averageCompletedTaskDuration.Value;
                hasEstimate = true;
            }
        }

        return hasEstimate ? totalRemaining : null;
    }

    private static TimeSpan? ComputeRemainingRevisionTime(int revisionsWritten, int estimatedTotalRevisions, double avgRevDurationMs)
    {
        if (avgRevDurationMs <= 0 || estimatedTotalRevisions <= 0 || revisionsWritten <= 0)
            return null;

        var remainingRevisions = Math.Max(0, estimatedTotalRevisions - revisionsWritten);
        return remainingRevisions > 0
            ? TimeSpan.FromMilliseconds(remainingRevisions * avgRevDurationMs)
            : TimeSpan.Zero;
    }

    private static string FormatEta(TimeSpan eta) =>
        eta.TotalHours >= 1
            ? $"{(int)eta.TotalHours}:{eta.Minutes:D2}:{eta.Seconds:D2}"
            : $"--:{eta.Minutes:D2}:{eta.Seconds:D2}";

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

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

    // ── Discovery display helpers ─────────────────────────────────────────────────────────

    /// <summary>Extracts the module segment (parts[1]) from a task ID like <c>capture.workitems.orgslug.project</c>.</summary>
    private static string GetDiscoveryTaskModule(string taskId)
    {
        var parts = taskId.Split('.');
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    /// <summary>Derives a short organisation label from a URL (last path segment or host).</summary>
    private static string DeriveDiscoveryOrgLabel(string orgUrl)
    {
        if (string.IsNullOrWhiteSpace(orgUrl)) return "(unknown)";
        if (!Uri.TryCreate(orgUrl, UriKind.Absolute, out var uri)) return orgUrl;
        var seg = uri.AbsolutePath.Trim('/').Split('/');
        return seg.Length > 0 && !string.IsNullOrWhiteSpace(seg[^1]) ? seg[^1] : uri.Host;
    }

    private static string DiscoveryCapFirst(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static IRenderable BuildDiscoveryTaskCell(JobTask task, string spinnerFrame)
    {
        var (icon, color) = task.Status switch
        {
            JobTaskStatus.Running => (spinnerFrame, "blue"),
            JobTaskStatus.Completed => ("✓", "green"),
            JobTaskStatus.Failed => ("✗", "red"),
            JobTaskStatus.Skipped => ("→", "grey"),
            _ => ("·", "grey"),
        };
        return new Markup($"[{color}]{icon}[/]");
    }

    // ── Discovery progress state (MVU) ────────────────────────────────────────────────────
    // Analysis jobs (Inventory, Dependencies) render a data table — one row per org/project —
    // updated on every batch notification. This is distinct from migration jobs which render
    // a task list with progress bars (BuildUnifiedTaskDisplay).

    private record InventoryProjectRow(
        string OrgUrl,
        string ProjectName,
        long WorkItems,
        long Revisions,
        int Repos,
        long ExternalLinks,
        long CrossProjectLinks,
        long CrossOrgLinks,
        long WorkItemsAnalysed,
        DateTimeOffset? StartedAt,
        string? StageText,
        bool IsCounting,
        bool IsComplete,
        bool IsFailed,
        bool IsInProgress);

    private sealed class OrgProjectKeyComparer : IEqualityComparer<(string Org, string Project)>
    {
        public bool Equals((string Org, string Project) x, (string Org, string Project) y) =>
            string.Equals(x.Org.TrimEnd('/'), y.Org.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Project, y.Project, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Org, string Project) obj) =>
            HashCode.Combine(
                obj.Org.TrimEnd('/').ToUpperInvariant(),
                obj.Project.ToUpperInvariant());
    }

    private record DiscoveryProgressState(
        JobTaskList? Tasks,
        IReadOnlyList<InventoryProjectRow> Projects,
        ProgressEvent? LastEvent)
    {
        public static DiscoveryProgressState Initial() =>
            new(null, Array.Empty<InventoryProjectRow>(), null);
    }

    private static DiscoveryProgressState ApplyDiscovery(DiscoveryProgressState state, JobProgressUpdate update) => update switch
    {
        TelemetryPolled => state,
        TimerTick => state,
        TaskListReceived t => ApplyDiscoveryTaskListSeedProjects(state, t.Tasks),
        TaskUpdated t => ApplyDiscoveryTaskUpdate(state, t.TaskId, t.Status),
        StageAdvanced t => ApplyDiscoveryStageAdvance(state, t.Event),
        SnapshotLoaded s => ApplyDiscoverySnapshot(state, s.Snapshot),
        _ => state
    };

    private static DiscoveryProgressState ApplyDiscoveryTaskUpdate(
        DiscoveryProgressState state, string taskId, JobTaskStatus newStatus)
    {
        if (state.Tasks is null) return state;
        var now = DateTimeOffset.UtcNow;
        var updated = state.Tasks.Tasks
            .Select(t => t.Id == taskId
                ? t with
                {
                    Status = newStatus,
                    StartedAt = newStatus == JobTaskStatus.Running ? t.StartedAt ?? now : t.StartedAt,
                    CompletedAt = newStatus is JobTaskStatus.Completed or JobTaskStatus.Failed or JobTaskStatus.Skipped
                        ? now : t.CompletedAt
                }
                : t)
            .ToList()
            .AsReadOnly();
        return state with { Tasks = state.Tasks with { Tasks = updated } };
    }

    /// <summary>
    /// Handles <see cref="TaskListReceived"/> by updating <see cref="DiscoveryProgressState.Tasks"/>
    /// and pre-seeding one pending project row per task that carries org/project info.
    /// This ensures all configured projects appear immediately when the bootstrap response
    /// arrives, even before any SSE events have been emitted for those projects.
    /// Existing rows (already added by SSE events) are never overwritten.
    /// </summary>
    private static DiscoveryProgressState ApplyDiscoveryTaskListSeedProjects(
        DiscoveryProgressState state, JobTaskList taskList)
    {
        var projects = new List<InventoryProjectRow>(state.Projects);
        var comparer = new OrgProjectKeyComparer();

        foreach (var task in taskList.Tasks)
        {
            if (task.OrganisationUrl is null || task.ProjectName is null) continue;

            var key = (Org: task.OrganisationUrl, Project: task.ProjectName);
            if (projects.Any(p => comparer.Equals((p.OrgUrl, p.ProjectName), key))) continue;

            projects.Add(new InventoryProjectRow(
                task.OrganisationUrl, task.ProjectName,
                WorkItems: task.KnownTotal ?? 0,
                Revisions: 0, Repos: 0,
                ExternalLinks: 0, CrossProjectLinks: 0, CrossOrgLinks: 0,
                WorkItemsAnalysed: 0, StartedAt: null,
                StageText: "Pending",
                IsCounting: false, IsComplete: false, IsFailed: false, IsInProgress: false));
        }

        return state with { Tasks = taskList, Projects = projects.AsReadOnly() };
    }

    private static DiscoveryProgressState ApplyDiscoveryStageAdvance(
        DiscoveryProgressState state, ProgressEvent evt)
    {
        var newState = state with { LastEvent = evt };

        // Propagate task-level status transitions from the event.
        if (evt.TaskId is not null && evt.TaskStatus.HasValue)
            newState = ApplyDiscoveryTaskUpdate(newState, evt.TaskId, evt.TaskStatus.Value);

        // Parse discovery project data out of the progress message ("{url}|{projectName}").
        var message = evt.Message ?? string.Empty;
        var stage = evt.Stage ?? string.Empty;

        if (!message.Contains('|'))
            return newState;

        var sepIdx = message.IndexOf('|');
        var orgUrl = message[..sepIdx];
        var projectName = message[(sepIdx + 1)..];
        if (string.IsNullOrEmpty(orgUrl) || string.IsNullOrEmpty(projectName))
            return newState;

        var projects = new List<InventoryProjectRow>(newState.Projects);
        var idx = projects.FindIndex(p =>
            string.Equals(p.OrgUrl, orgUrl, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
        var existing = idx >= 0 ? projects[idx] : null;

        var isInventoryStage = stage is "Progress" or "Inventory" or "InventorySeed";
        var isDependencyStage = stage is "Counting" or "Analysis" or "ProjectComplete" or "Failed";

        if (!isInventoryStage && !isDependencyStage)
            return newState;

        var workItems = evt.Metrics?.Scope?.WorkItemsTotal ?? existing?.WorkItems ?? 0;
        var revisions = evt.Metrics?.Discovery?.Inventory?.RevisionsTotal ?? existing?.Revisions ?? 0;
        var repos = (int)(evt.Metrics?.Discovery?.Inventory?.RepositoriesTotal ?? existing?.Repos ?? 0);
        var externalLinks = evt.Metrics?.Discovery?.Dependencies?.ExternalLinksFound ?? existing?.ExternalLinks ?? 0;
        var crossProjectLinks = evt.Metrics?.Discovery?.Dependencies?.CrossProjectLinks ?? existing?.CrossProjectLinks ?? 0;
        var crossOrgLinks = evt.Metrics?.Discovery?.Dependencies?.CrossOrgLinks ?? existing?.CrossOrgLinks ?? 0;
        var workItemsAnalysed = evt.Metrics?.Discovery?.Dependencies?.WorkItemsAnalysed ?? existing?.WorkItemsAnalysed ?? 0;

        // Capture the wall-clock start the first time we enter analysis so ETA can be computed.
        var isAnalysing = stage is "Analysis";
        var startedAt = isAnalysing
            ? existing?.StartedAt ?? DateTimeOffset.UtcNow
            : existing?.StartedAt;

        var row = new InventoryProjectRow(
            orgUrl,
            projectName,
            workItems,
            revisions,
            repos,
            externalLinks,
            crossProjectLinks,
            crossOrgLinks,
            workItemsAnalysed,
            startedAt,
            StageText: stage,
            IsCounting: stage == "Counting",
            IsComplete: stage is "Inventory" or "ProjectComplete",
            IsFailed: stage == "Failed",
            IsInProgress: stage is "Progress" or "Analysis" or "Counting");

        if (idx >= 0)
        {
            projects[idx] = row;
        }
        else
        {
            projects.Add(row);
        }

        return newState with { Projects = projects.AsReadOnly() };
    }

    /// <summary>
    /// Pre-populates the discovery table from a <see cref="JobSnapshot"/> returned by the
    /// bootstrap endpoint. Used for resuming jobs where SSE catchup events may have been
    /// evicted from the ring buffer before the client connected. Existing rows (added by
    /// live SSE events) take precedence — snapshot rows are only inserted, never updated.
    /// </summary>
    private static DiscoveryProgressState ApplyDiscoverySnapshot(DiscoveryProgressState state, JobSnapshot snapshot)
    {
        if (snapshot.Organisations.Count == 0)
            return state;

        var projects = new List<InventoryProjectRow>(state.Projects);

        foreach (var org in snapshot.Organisations)
        {
            foreach (var project in org.Projects)
            {
                // Do not overwrite a row that was already added by a live SSE event.
                var exists = projects.Any(p =>
                    string.Equals(p.OrgUrl, org.Url, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase));

                if (exists) continue;

                var workItems = project.Discovery?.Inventory?.WorkItemsTotal ?? 0;
                var revisions = project.Discovery?.Inventory?.RevisionsTotal ?? 0;
                var repos = (int)(project.Discovery?.Inventory?.RepositoriesTotal ?? 0);

                projects.Add(new InventoryProjectRow(
                    org.Url, project.Name,
                    workItems, revisions, repos,
                    ExternalLinks: project.Discovery?.Dependencies?.ExternalLinksFound ?? 0,
                    CrossProjectLinks: project.Discovery?.Dependencies?.CrossProjectLinks ?? 0,
                    CrossOrgLinks: project.Discovery?.Dependencies?.CrossOrgLinks ?? 0,
                    WorkItemsAnalysed: 0, StartedAt: null,
                    StageText: project.Status == ProjectStatus.Completed ? "ProjectComplete" : project.Status.ToString(),
                    IsCounting: false,
                    IsComplete: project.Status == ProjectStatus.Completed,
                    IsFailed: project.Status == ProjectStatus.Failed,
                    IsInProgress: project.Status == ProjectStatus.InProgress));
            }
        }

        return state with { Projects = projects.AsReadOnly() };
    }


    /// Columns: Org | Project | module-status cells | Work Items | Revisions | Repos | Time
    /// The task-status icons and the inventory data counts are merged per row.
    /// </summary>
    private static IRenderable BuildDiscoveryDisplay(DiscoveryProgressState s)
    {
        var spinnerFrame = s_spinnerFrames[(int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 80 % s_spinnerFrames.Length)];
        var rows = new List<IRenderable>();
        var hasDependencyProjectState = s.Projects.Any(project =>
            project.IsCounting ||
            project.WorkItemsAnalysed > 0 ||
            project.ExternalLinks > 0 ||
            project.CrossProjectLinks > 0 ||
            project.CrossOrgLinks > 0 ||
            project.StageText is "Analysis" or "Counting" or "ProjectComplete");

        if (s.Tasks is null && s.Projects.Count == 0)
        {
            rows.Add(new Markup("[grey]⠋ Initialising — waiting for agent to start…[/]"));
        }
        else if (s.Tasks is not null && s.Tasks.Tasks.Count > 0)
        {
            var captureTasks = s.Tasks.Tasks
                .Where(t => t.TaskKind == TaskKind.Capture)
                .ToList();
            var analyseTasks = s.Tasks.Tasks
                .Where(t => t.TaskKind == TaskKind.Analyse)
                .OrderBy(t => t.Order)
                .ToList();

            if (captureTasks.Count > 0 && !hasDependencyProjectState)
            {
                // Determine which module columns are active in this job.
                var moduleOrder = new[] { "identities", "nodes", "teams", "workitems", "repos" };
                var activeModules = moduleOrder
                    .Where(m => captureTasks.Any(t => GetDiscoveryTaskModule(t.Id) == m))
                    .ToArray();

                // Build a lookup from (orgUrl, projectName) → InventoryProjectRow for count data.
                var countsByKey = s.Projects.ToDictionary(
                    p => (Org: p.OrgUrl, Project: p.ProjectName),
                    p => p,
                    new OrgProjectKeyComparer());

                var table = new Table()
                    .BorderStyle(Style.Parse("grey"))
                    .AddColumn(new TableColumn("[grey]Organisation[/]"))
                    .AddColumn(new TableColumn("[grey]Project[/]"));
                foreach (var mod in activeModules)
                    table.AddColumn(new TableColumn($"[grey]{DiscoveryCapFirst(mod)}[/]").Centered());
                table.AddColumn(new TableColumn("[grey]Work Items[/]").RightAligned());
                table.AddColumn(new TableColumn("[grey]Revisions[/]").RightAligned());
                table.AddColumn(new TableColumn("[grey]Repos[/]").RightAligned());
                table.AddColumn(new TableColumn("[grey]Time[/]").RightAligned());

                var groups = captureTasks
                    .GroupBy(t => (Org: t.OrganisationUrl ?? string.Empty, Project: t.ProjectName ?? string.Empty))
                    .OrderBy(g => g.Key.Org)
                    .ThenBy(g => g.Key.Project);

                string? lastOrg = null;
                long totalWi = 0, totalRev = 0;

                foreach (var grp in groups)
                {
                    var orgLabel = grp.Key.Org != lastOrg
                        ? TruncateName(DeriveDiscoveryOrgLabel(grp.Key.Org), 36)
                        : string.Empty;
                    lastOrg = grp.Key.Org;

                    var grpList = grp.ToList();
                    var cells = new List<IRenderable>
                    {
                        new Markup(string.IsNullOrEmpty(orgLabel) ? string.Empty : $"[grey]{Markup.Escape(orgLabel)}[/]"),
                        new Markup(Markup.Escape(grp.Key.Project))
                    };

                    foreach (var mod in activeModules)
                    {
                        var t = grpList.FirstOrDefault(x => GetDiscoveryTaskModule(x.Id) == mod);
                        cells.Add(t is null ? new Markup("[grey]–[/]") : BuildDiscoveryTaskCell(t, spinnerFrame));
                    }

                    // Merge count data from the InventoryProjectRow if available.
                    countsByKey.TryGetValue((grp.Key.Org, grp.Key.Project), out var counts);
                    var wi = counts?.WorkItems ?? 0;
                    var rev = counts?.Revisions ?? 0;
                    var repos = counts?.Repos ?? 0;
                    totalWi += wi;
                    totalRev += rev;
                    cells.Add(new Markup(wi > 0 ? $"[bold]{wi:N0}[/]" : "[grey]–[/]"));
                    cells.Add(new Markup(rev > 0 ? $"{rev:N0}" : "[grey]–[/]"));
                    cells.Add(new Markup(repos > 0 ? $"{repos:N0}" : "[grey]–[/]"));

                    var anyRunning = grpList.Any(t => t.Status == JobTaskStatus.Running);
                    var allSettled = grpList.All(t =>
                        t.Status is JobTaskStatus.Completed or JobTaskStatus.Failed or JobTaskStatus.Skipped);
                    var maxSec = grpList
                        .Where(t => t.StartedAt.HasValue && t.CompletedAt.HasValue)
                        .Select(t => (t.CompletedAt!.Value - t.StartedAt!.Value).TotalSeconds)
                        .DefaultIfEmpty(0).Max();
                    string timeCell = anyRunning
                        ? $"[blue]{spinnerFrame}[/]"
                        : allSettled && maxSec > 0
                            ? $"[grey]{maxSec:F0}s[/]"
                            : string.Empty;
                    cells.Add(new Markup(timeCell));

                    table.AddRow(cells.ToArray());
                }

                // Totals footer row (only when we have count data).
                if (totalWi > 0)
                {
                    table.AddEmptyRow();
                    var totalCells = new List<IRenderable>
                    {
                        new Markup("[bold]Total[/]"),
                        new Markup(string.Empty)
                    };
                    foreach (var _ in activeModules)
                        totalCells.Add(new Markup(string.Empty));
                    totalCells.Add(new Markup($"[bold]{totalWi:N0}[/]"));
                    totalCells.Add(new Markup(totalRev > 0 ? $"[bold]{totalRev:N0}[/]" : "[grey]–[/]"));
                    totalCells.Add(new Markup(string.Empty));
                    totalCells.Add(new Markup(string.Empty));
                    table.AddRow(totalCells.ToArray());
                }

                // Analyse tasks as footer rows.
                foreach (var at in analyseTasks)
                {
                    var (icon, color) = at.Status switch
                    {
                        JobTaskStatus.Running => (spinnerFrame, "blue"),
                        JobTaskStatus.Completed => ("✓", "green"),
                        JobTaskStatus.Failed => ("✗", "red"),
                        JobTaskStatus.Skipped => ("→", "grey"),
                        _ => ("·", "grey"),
                    };
                    var atCells = new List<IRenderable>
                    {
                        new Markup($"[{color}]{icon}[/] [bold]{Markup.Escape(at.Name)}[/]"),
                        new Markup(string.Empty)
                    };
                    // Fill remaining columns: module cells + workitems + revisions + repos + time
                    for (int i = 0; i < activeModules.Length + 4; i++)
                        atCells.Add(new Markup(string.Empty));
                    table.AddRow(atCells.ToArray());
                }

                rows.Add(table);
            }
            else if (s.Projects.Count > 0)
            {
                rows.Add(BuildDependenciesProjectTable(s.Projects, spinnerFrame));
            }
            else
            {
                // Non-capture job: render flat list (dependencies, etc.)
                foreach (var task in s.Tasks.Tasks.OrderBy(t => t.Order))
                {
                    var (icon, iconColor) = task.Status switch
                    {
                        JobTaskStatus.Running => (spinnerFrame, "blue"),
                        JobTaskStatus.Completed => ("✓", "green"),
                        JobTaskStatus.Failed => ("✗", "red"),
                        JobTaskStatus.Skipped => ("→", "grey"),
                        _ => ("·", "grey"),
                    };
                    rows.Add(new Markup($"  [{iconColor}]{icon}[/]  [bold]{Markup.Escape(task.Name)}[/]"));
                }
            }
        }
        else if (s.Projects.Count > 0)
        {
            rows.Add(BuildDependenciesProjectTable(s.Projects, spinnerFrame));
        }

        return rows.Count > 0 ? (IRenderable)new Rows(rows) : new Markup("[grey]⠋ Running…[/]");
    }

    private static IRenderable BuildDependenciesProjectTable(
        IReadOnlyList<InventoryProjectRow> projects,
        string spinnerFrame)
    {
        var table = new Table()
            .BorderStyle(Style.Parse("grey"))
            .AddColumn(new TableColumn("[grey]Organisation[/]"))
            .AddColumn(new TableColumn("[grey]Project[/]"))
            .AddColumn(new TableColumn("[grey]Status[/]").Centered())
            .AddColumn(new TableColumn("[grey]Work Items[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Links[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Cross Project[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Cross Org[/]").RightAligned())
            .AddColumn(new TableColumn("[grey]Remaining[/]").RightAligned());

        string? lastOrg = null;
        foreach (var project in projects
                     .OrderBy(p => p.OrgUrl, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase))
        {
            var orgLabel = project.OrgUrl != lastOrg
                ? TruncateName(DeriveDiscoveryOrgLabel(project.OrgUrl), 36)
                : string.Empty;
            lastOrg = project.OrgUrl;

            var (icon, color, statusText) = project switch
            {
                { IsFailed: true } => ("✗", "red", "Failed"),
                { IsComplete: true } => ("✓", "green", "Complete"),
                { IsCounting: true } => (spinnerFrame, "blue", "Counting"),
                { IsInProgress: true } => (spinnerFrame, "blue", "Analysing"),
                _ => ("·", "grey", project.StageText ?? "Pending"),
            };

            // Work Items column: show progress during active phases
            string wiCell;
            if (project.IsCounting && project.WorkItems > 0)
            {
                wiCell = $"[blue]{project.WorkItems:N0}…[/]";
            }
            else if (project.IsInProgress && project.WorkItems > 0 && project.WorkItemsAnalysed > 0)
            {
                wiCell = $"[bold]{project.WorkItemsAnalysed:N0}[/][grey]/{project.WorkItems:N0}[/]";
            }
            else
            {
                wiCell = project.WorkItems > 0 ? $"[bold]{project.WorkItems:N0}[/]" : "[grey]–[/]";
            }

            // Remaining column: ETA based on elapsed time and rate
            string etaCell = string.Empty;
            if (project.IsInProgress && project.StartedAt.HasValue
                && project.WorkItemsAnalysed > 0 && project.WorkItems > project.WorkItemsAnalysed)
            {
                var elapsed = (DateTimeOffset.UtcNow - project.StartedAt.Value).TotalSeconds;
                if (elapsed > 0)
                {
                    var rate = project.WorkItemsAnalysed / elapsed;
                    var remainingSec = (project.WorkItems - project.WorkItemsAnalysed) / rate;
                    var span = TimeSpan.FromSeconds(remainingSec);
                    etaCell = span.TotalHours >= 1
                        ? $"[grey]{span:h\\:mm\\:ss}[/]"
                        : $"[grey]{span:m\\:ss}[/]";
                }
            }
            else if (project.IsCounting)
            {
                etaCell = "[grey]…[/]";
            }

            table.AddRow(
                new Markup(string.IsNullOrEmpty(orgLabel) ? string.Empty : $"[grey]{Markup.Escape(orgLabel)}[/]"),
                new Markup(Markup.Escape(project.ProjectName)),
                new Markup($"[{color}]{icon}[/] [grey]{Markup.Escape(statusText)}[/]"),
                new Markup(wiCell),
                new Markup(project.ExternalLinks > 0 ? $"[bold]{project.ExternalLinks:N0}[/]" : "[grey]0[/]"),
                new Markup(project.CrossProjectLinks > 0 ? $"{project.CrossProjectLinks:N0}" : "[grey]0[/]"),
                new Markup(project.CrossOrgLinks > 0 ? $"{project.CrossOrgLinks:N0}" : "[grey]0[/]"),
                new Markup(etaCell));
        }

        return table;
    }

    /// <summary>
    /// Converts <see cref="MigrationPlatformOptions.Modules"/> into <see cref="JobModule"/> entries.
    /// Reads from the typed static module configuration and builds the runtime job contract.
    /// </summary>
}
