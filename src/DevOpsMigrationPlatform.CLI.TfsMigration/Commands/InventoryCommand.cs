using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using Spectre.Console.Cli;
using SpectreValidationResult = Spectre.Console.ValidationResult;

namespace DevOpsMigrationPlatform.CLI.TfsMigration.Commands
{
    /// <summary>
    /// <c>tfsmigration inventory</c> — counts work items per project using
    /// date-chunked WIQL queries.  Credentials are read from a single JSON line on stdin:
    /// <c>{"pat":"..."}</c> for PAT auth, or <c>{}</c> for Windows-integrated auth.
    /// Progress is emitted as NDJSON <see cref="InventoryProgressEvent"/> records on stdout.
    /// </summary>
    public sealed class InventoryCommand : TfsCommandBase<InventoryCommand.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [System.ComponentModel.Description("URL of the TFS / Azure DevOps Server collection")]
            [CommandOption("--collection <COLLECTION>")]
            public string CollectionUrl { get; set; } = string.Empty;

            [System.ComponentModel.Description("Team project name to inventory")]
            [CommandOption("--project <PROJECT>")]
            public string? Project { get; set; }

            [System.ComponentModel.Description("Inventory all projects in the collection")]
            [CommandOption("--all-projects")]
            public bool AllProjects { get; set; }

            public override SpectreValidationResult Validate()
            {
                if (string.IsNullOrWhiteSpace(CollectionUrl))
                    return SpectreValidationResult.Error("--collection is required");

                if (!Uri.TryCreate(CollectionUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return SpectreValidationResult.Error("--collection must be a valid http or https URL");

                if (!AllProjects && string.IsNullOrWhiteSpace(Project))
                    return SpectreValidationResult.Error("Either --project or --all-projects is required");

                return SpectreValidationResult.Success();
            }
        }

        protected override async Task<int> ExecuteInternalAsync(
            CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var pat = await ReadCredentialsFromStdinAsync().ConfigureAwait(false);

            // Inventory doesn't write a package — use a temp folder for the required OutputFolder.
            var tempOutput = Path.Combine(Path.GetTempPath(), "tfsmigration-inventory-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var hostSettings = new MigrationPlatformHost.Settings(
                new Uri(settings.CollectionUrl),
                settings.Project ?? string.Empty,
                tempOutput);

            await CreateHost(hostSettings, context.Arguments.ToArray()).ConfigureAwait(false);

            var discoveryService = GetRequiredService<IWorkItemDiscoveryService>();
            var sink = new StdoutInventoryProgressSink();

            var endpoint = new Infrastructure.TfsObjectModel.Options.TeamFoundationServerEndpointOptions
            {
                Url = settings.CollectionUrl,
                Type = "TfsObjectModel",
                Authentication = new EndpointAuthenticationOptions
                {
                    Type = string.IsNullOrEmpty(pat) ? AuthenticationType.Windows : AuthenticationType.Pat,
                    AccessToken = pat ?? string.Empty
                }
            };

            var orgEndpoint = endpoint.ToOrganisationEndpoint();

            IEnumerable<string> projectNames;
            if (settings.AllProjects)
            {
                var projectDiscovery = GetRequiredService<IProjectDiscoveryService>();
                projectNames = await projectDiscovery.DiscoverProjectsAsync(
                    endpoint, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                projectNames = new[] { settings.Project! };
            }

            foreach (var projName in projectNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunProjectInventoryAsync(
                    discoveryService, sink, orgEndpoint, projName,
                    cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }

        private static async Task RunProjectInventoryAsync(
            IWorkItemDiscoveryService discoveryService,
            StdoutInventoryProgressSink sink,
            OrganisationEndpoint endpoint,
            string project,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var summary in discoveryService
                    .CountWorkItemsAsync(endpoint, project, cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                {
                    sink.Emit(new InventoryProgressEvent
                    {
                        ProjectName = project,
                        Url = endpoint.ResolvedUrl,
                        WorkItemsCount = summary.WorkItemsCount,
                        RevisionsCount = summary.RevisionsCount,
                        IsComplete = summary.IsWorkItemComplete,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sink.Emit(new InventoryProgressEvent
                {
                    ProjectName = project,
                    Url = endpoint.ResolvedUrl,
                    IsComplete = true,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }
}
