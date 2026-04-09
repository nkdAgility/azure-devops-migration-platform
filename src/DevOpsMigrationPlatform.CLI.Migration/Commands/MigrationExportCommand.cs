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
using DevOpsMigrationPlatform.Infrastructure.Services;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Builds a <see cref="MigrationJob"/> from the migration config file and submits it
/// to the control plane via <see cref="IJobRunner"/> (ControlPlaneClient).
///
/// When <c>config.Source.Type == "TeamFoundationServer"</c> the command transparently
/// delegates to <see cref="TfsExportRunner"/> (the net481 subprocess bridge) instead of
/// submitting a job to the control plane. This keeps TFS OM handling invisible to the
/// operator: a single <c>devopsmigration export --config ...</c> command handles both
/// Azure DevOps Services and on-premises TFS/Azure DevOps Server.
///
/// No migration logic runs in this command — all execution happens in the agent (Azure DevOps path)
/// or the TFS subprocess (TFS path).
/// See docs/cli.md and system-architecture guardrail rules 16 and 19.
/// </summary>
public sealed class MigrationExportCommand : ControlPlaneCommandBase<MigrationExportCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        MigrationExportCommandSettings settings,
        CancellationToken cancellationToken = default)
    {
        var resolvedUrl = MigrationPlatformHost.ResolveControlPlaneUrl(settings.Url);

        await CreateHost(Environment.GetCommandLineArgs(), resolvedUrl, (services, config) =>
        {
            services.AddSingleton<IConfigurationService, ConfigurationService>();

            services.AddOptions<ControlPlaneOptions>()
                .BindConfiguration(ControlPlaneOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHttpClient<ControlPlaneClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<ControlPlaneOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
            });

            services.AddTransient<IJobRunner>(sp => sp.GetRequiredService<ControlPlaneClient>());

            // TFS subprocess services — registered unconditionally so DI resolves them
            // correctly when the source type is TeamFoundationServer.
            services.AddSingleton<IProgressSink, AnsiProgressSink>();
            services.AddSingleton<TfsExporterProcessAdapter>();
            services.AddSingleton<IExternalToolRunner, ExternalToolRunner>();
        });

        var config = await LoadConfigurationAsync(settings, cancellationToken);
        if (config is null)
            return 1;

        // Delegate to the TFS subprocess runner for on-premises sources.
        if (string.Equals(config.Source?.Type, "TeamFoundationServer", StringComparison.Ordinal))
            return await TfsExportRunner.RunAsync(config, Host!.Services, tfsExportExePathOverride: null, cancellationToken);

        return await ExecuteAdoExportAsync(config, settings, cancellationToken);
    }

    private async Task<int> ExecuteAdoExportAsync(MigrationOptions config, MigrationExportCommandSettings settings, CancellationToken cancellationToken)
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

        // Build MigrationJob — no migration logic here.
        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            ConfigVersion = config.ConfigVersion,
            Mode = "Export",
            Source = new MigrationJobEndpoint
            {
                Type = config.Source!.Type,
                Url = orgUrl,
                Project = project,
                ApiVersion = config.Source.ApiVersion,
                Authentication = config.Source.Authentication
            },
            Artefacts = new MigrationJobArtefacts
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                Zip = config.Artefacts.Zip
            },
            Modules = modules,
            Guardrails = new MigrationJobGuardrails
            {
                StreamingRequired = true,
                CanonicalWorkItemsLayoutRequired = true
            },
            Diagnostics = new MigrationJobDiagnostics
            {
                MinimumLevel = settings.Level
            }
        };

        // Determine follow mode: explicit --follow, or implicit in standalone mode (no --url).
        var isStandaloneMode = string.IsNullOrEmpty(settings.Url);
        var shouldFollow = settings.Follow || isStandaloneMode;

        var client = GetRequiredService<ControlPlaneClient>();

        // Non-follow remote mode: submit and exit immediately (FR-025).
        if (!shouldFollow)
        {
            var jobId = await client.SubmitAsync(job, cancellationToken);
            console.MarkupLine($"[green]✓[/] Job [bold]{jobId}[/] submitted. Use [blue]manage status --job {jobId}[/] to check progress.");
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
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to submit job: {ex.Message}");
            return 1;
        }

        // Start diagnostics streaming as a background task.
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
                    console.MarkupLine($"[{levelColor}]{Markup.Escape(record.Level)}[/] [{Markup.Escape(record.Category)}] {Markup.Escape(record.Message)}");
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

        // Stream progress in the foreground.
        try
        {
            await console.Live(new Markup("[grey]Waiting for agent...[/]"))
                .StartAsync(async ctx =>
                {
                    await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
                    {
                        lastEvt = evt;
                        if (evt.RevisionsProcessed > 0)
                            ctx.UpdateTarget(new Markup(
                                $"[blue]WorkItems[/]  [bold]{evt.WorkItemsProcessed}[/] work items / [bold]{evt.RevisionsProcessed}[/] revisions  [grey](wi#{evt.WorkItemId})[/]"));
                        else if (!string.IsNullOrEmpty(evt.Message))
                            ctx.UpdateTarget(new Markup($"[grey]{Markup.Escape(evt.Message)}[/]"));
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

        // Stop diagnostics stream.
        await followCts.CancelAsync();
        try { await diagnosticsTask; } catch (OperationCanceledException) { }

        if (jobFailed)
            return 1;

        if (lastEvt is not null)
            ShowSuccess(console, $"Export complete — {lastEvt.WorkItemsProcessed} work items / {lastEvt.RevisionsProcessed} revisions written to package.");
        else
            ShowSuccess(console, "Work item export complete.");
        return 0;
    }

    /// <summary>
    /// Converts <see cref="MigrationOptions.Modules"/> into <see cref="MigrationJobModule"/> entries.
    /// If the config has no modules, a default WorkItems/wiql scope is injected to preserve
    /// backward compatibility with pre-modules config files.
    /// </summary>
    private static List<MigrationJobModule> BuildModules(MigrationOptions config)
    {
        if (config.Modules.Count > 0)
        {
            return config.Modules
                .Where(m => m.Enabled)
                .Select(m => new MigrationJobModule
                {
                    Name = m.Name,
                    Scopes = m.Scopes
                        .Select(s => new MigrationJobModuleScope
                        {
                            Type = s.Type,
                            Parameters = s.Parameters
                                .ToDictionary(
                                    kv => kv.Key,
                                    kv => (object?)kv.Value.ToString())
                        })
                        .ToList()
                })
                .ToList();
        }

        // Default: WorkItems module with platform-default WIQL scope.
        return
        [
            new MigrationJobModule
            {
                Name   = "WorkItems",
                Scopes =
                [
                    new MigrationJobModuleScope
                    {
                        Type       = "wiql",
                        Parameters = new Dictionary<string, object?>
                        {
                            ["query"]              = Infrastructure.Modules.WorkItemsScopeParameters.DefaultWiqlQuery,
                            ["includeRevisions"]   = true,
                            ["includeLinks"]       = true,
                            ["includeAttachments"] = true
                        }
                    }
                ]
            }
        ];
    }
}

