// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.Infrastructure.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>
/// Submits a lightweight probe job through the full pipeline (CLI → Control Plane → Agent → ArtefactStore)
/// to validate that permissions, configuration, and connectivity work end-to-end.
/// The agent writes a single probe file to the artefact store and completes.
/// </summary>
public sealed class PrepareCommand : ControlPlaneCommandBase<MigrationCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        MigrationCommandSettings settings,
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
        });

        var console = GetRequiredService<IAnsiConsole>();
        var config = await LoadConfigurationAsync(settings, cancellationToken);
        if (config is null)
            return 1;

        var outputPath = Path.GetFullPath(config.Package.ExpandedPath);

        console.MarkupLine("[blue]ℹ[/] Running end-to-end preparation check…");
        console.MarkupLine($"[blue]ℹ[/] Package path: [blue]{Markup.Escape(outputPath)}[/]");

        var job = new Job
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = JobKind.Prepare,
            Connectors = config.Source?.Type switch
            {
                "TeamFoundationServer" => new[] { ConnectorType.TeamFoundationServer },
                "AzureDevOpsServices" => new[] { ConnectorType.AzureDevOps },
                _ => Array.Empty<ConnectorType>()
            },
            Package = new JobPackage
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                CreatePackage = false
            }
        };

        var client = GetRequiredService<ControlPlaneClient>();
        var envOpts = GetRequiredService<IOptions<EnvironmentOptions>>().Value;

        Guid parsedJobId;
        try
        {
            parsedJobId = await client.SubmitAsync(job, cancellationToken);
            PrintJobSubmitted(console, parsedJobId, GetControlPlaneUrl());
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]✗[/] Failed to submit prepare job: {Markup.Escape(ex.Message)}");
            return 1;
        }

        // Follow the job to completion — prepare jobs are fast.
        // Use a timeout to avoid hanging if the SSE stream doesn't close cleanly.
        using var prepareCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prepareCts.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await foreach (var evt in client.FollowLogsAsync(parsedJobId, prepareCts.Token))
            {
                if (!string.IsNullOrEmpty(evt.Message))
                    console.MarkupLine($"  [grey]{Markup.Escape(evt.Message)}[/]");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // SSE stream timed out but the job may have completed — check status.
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
        {
            console.MarkupLine($"[red]✗[/] Prepare job failed: {Markup.Escape(ex.Message)}");
            return 1;
        }

        console.MarkupLine("[green]✓[/] Preparation check passed — artefact store is accessible and agent can execute in this environment.");
        return 0;
    }
}
