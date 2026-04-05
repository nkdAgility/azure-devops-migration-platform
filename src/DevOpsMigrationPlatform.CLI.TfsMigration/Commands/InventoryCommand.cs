using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console.Cli;
using SpectreValidationResult = Spectre.Console.ValidationResult;

namespace DevOpsMigrationPlatform.CLI.TfsMigration.Commands
{
    /// <summary>
    /// <c>tfsmigration inventory</c> — counts work items per project using
    /// date-chunked WIQL queries.  Credentials are read from a single JSON line on stdin:
    /// <c>{"pat":"..."}</c> for PAT auth, or <c>{}</c> for Windows-integrated auth.
    /// Progress is emitted as NDJSON <see cref="DevOpsMigrationPlatform.Abstractions.Models.InventoryProgressEvent"/> records on stdout.
    /// </summary>
    public sealed class InventoryCommand : AsyncCommand<InventoryCommand.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [System.ComponentModel.Description("URL of the TFS / Azure DevOps Server collection")]
            [Spectre.Console.Cli.CommandOption("--collection <COLLECTION>")]
            public string CollectionUrl { get; set; } = string.Empty;

            [System.ComponentModel.Description("Team project name to inventory")]
            [Spectre.Console.Cli.CommandOption("--project <PROJECT>")]
            public string? Project { get; set; }

            [System.ComponentModel.Description("Inventory all projects in the collection")]
            [Spectre.Console.Cli.CommandOption("--all-projects")]
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

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {
            // Read one JSON line from stdin for credentials.
            // {"pat":"<token>"} — PAT auth
            // {}                — Windows-integrated auth (empty or missing pat)
            string? pat = null;
            try
            {
                var stdinLine = await Console.In.ReadLineAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(stdinLine))
                {
                    using var doc = JsonDocument.Parse(stdinLine);
                    if (doc.RootElement.TryGetProperty("pat", out var patProp))
                        pat = patProp.GetString();
                }
            }
            catch
            {
                // No stdin or malformed JSON — fall back to Windows-integrated auth.
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var agent = new TfsInventoryAgent();
            agent.Run(settings.CollectionUrl, settings.Project, pat, settings.AllProjects, cts.Token);

            return 0;
        }
    }
}
