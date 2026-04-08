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
/// ADO Services and on-premises TFS/Azure DevOps Server.
///
/// No migration logic runs in this command — all execution happens in the agent (ADO path)
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

        return await ExecuteAdoExportAsync(config, cancellationToken);
    }

    private async Task<int> ExecuteAdoExportAsync(MigrationOptions config, CancellationToken cancellationToken)
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
            }
        };

        var jobRunner = GetRequiredService<IJobRunner>();

        await foreach (var evt in jobRunner.RunAsync(job, cancellationToken).ConfigureAwait(false))
        {
            console.MarkupLine($"[grey]{Markup.Escape(evt.Message ?? string.Empty)}[/]");
        }

        console.MarkupLine("[green]✓[/] Work item export complete.");
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

