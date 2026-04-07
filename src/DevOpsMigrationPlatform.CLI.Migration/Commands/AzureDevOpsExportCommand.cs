using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Builds a <see cref="MigrationJob"/> from the migration config file and submits it
/// to the control plane via <see cref="IJobRunner"/> (ControlPlaneClient).
/// No migration logic runs in this command — all execution happens in the agent.
/// See docs/cli.md and system-architecture guardrail rule 16.
/// </summary>
public sealed class AzureDevOpsExportCommand : CommandBase<ExportCommandSettings>
{
    private const string DefaultWiqlQuery =
        "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";

    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        ExportCommandSettings settings,
        CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
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
        });

        var config = await LoadConfigurationAsync(settings, cancellationToken);
        if (config is null)
            return 1;

        var console = GetRequiredService<IAnsiConsole>();

        // Validate required source fields.
        var orgUrl  = settings.SourceUrl   ?? config.Source?.Url;
        var project = settings.ProjectName ?? config.Source?.Project;

        if (string.IsNullOrWhiteSpace(orgUrl))
        {
            ShowError(console, "Source URL is required. Set Source.Url in migration.json or pass it as the first argument.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            ShowError(console, "Project name is required. Set Source.Project in migration.json or pass --project.");
            return 1;
        }

        // Resolve output path.
        var outputPath = !string.IsNullOrWhiteSpace(settings.OutputPath)
            ? Path.GetFullPath(settings.OutputPath)
            : Path.GetFullPath(config.Artefacts.ExpandedPath);

        console.MarkupLine($"[blue]ℹ[/] Exporting from [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path  : [blue]{Markup.Escape(outputPath)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Attachments   : {(settings.IncludeAttachments ? "[green]included[/]" : "[grey]excluded[/]")}");

        // Build MigrationJob — no migration logic here.
        var job = new MigrationJob
        {
            JobId         = Guid.NewGuid().ToString(),
            ConfigVersion = config.ConfigVersion,
            Mode          = "Export",
            Source        = new MigrationJobEndpoint
            {
                Type           = config.Source?.Type ?? "AzureDevOpsServices",
                Url            = orgUrl,
                Project        = project,
                ApiVersion     = config.Source?.ApiVersion,
                Authentication = config.Source?.Authentication
            },
            Artefacts = new MigrationJobArtefacts
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                Zip        = config.Artefacts.Zip
            },
            Modules =
            [
                new MigrationJobModule
                {
                    Name   = "WorkItems",
                    Scopes =
                    [
                        new MigrationJobModuleScope
                        {
                            Type       = "wiql",
                            Parameters = new System.Collections.Generic.Dictionary<string, object?>
                            {
                                ["query"]              = DefaultWiqlQuery,
                                ["includeAttachments"] = settings.IncludeAttachments
                            }
                        }
                    ]
                }
            ],
            Guardrails = new MigrationJobGuardrails
            {
                StreamingRequired                  = true,
                CanonicalWorkItemsLayoutRequired   = true
            }
        };

        // Submit to control plane and stream progress.
        var jobRunner = GetRequiredService<IJobRunner>();

        await foreach (var evt in jobRunner.RunAsync(job, cancellationToken).ConfigureAwait(false))
        {
            console.MarkupLine($"[grey]{Markup.Escape(evt.Message ?? string.Empty)}[/]");
        }

        console.MarkupLine("[green]✓[/] Work item export complete.");
        return 0;
    }
}

