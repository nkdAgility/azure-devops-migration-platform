using System.Diagnostics;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using SpectreValidationResult = Spectre.Console.ValidationResult;

namespace DevOpsMigrationPlatform.CLI.TfsMigration.Commands
{
    public sealed class ExportCommand : TfsCommandBase<ExportCommand.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandOption("--collection <COLLECTION>")]
            [Description("URL of the TFS collection (e.g. http://tfs:8080/tfs/DefaultCollection)")]
            public string CollectionUrl { get; set; } = string.Empty;

            [CommandOption("--project <PROJECT>")]
            [Description("Team project name to export")]
            public string Project { get; set; } = string.Empty;

            [CommandOption("--output <OUTPUT>")]
            [Description("Root folder where the migration package will be written (default: ./package)")]
            public string OutputFolder { get; set; } = "./package";

            public override SpectreValidationResult Validate()
            {
                if (string.IsNullOrWhiteSpace(CollectionUrl))
                    return SpectreValidationResult.Error("--collection is required");

                if (!Uri.TryCreate(CollectionUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return SpectreValidationResult.Error("--collection must be a valid http or https URL");

                if (string.IsNullOrWhiteSpace(Project))
                    return SpectreValidationResult.Error("--project is required");

                try { Path.GetFullPath(OutputFolder); }
                catch (ArgumentException) { return SpectreValidationResult.Error("--output path is not valid"); }

                return SpectreValidationResult.Success();
            }
        }

        protected override async Task<int> ExecuteInternalAsync(
            CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var hostSettings = new MigrationPlatformHost.Settings(
                new Uri(settings.CollectionUrl),
                settings.Project,
                Path.GetFullPath(settings.OutputFolder));

            await CreateHost(hostSettings, context.Arguments.ToArray()).ConfigureAwait(false);

            // Restore W3C trace context propagated by the parent process via env var.
            // This makes all spans emitted by the subprocess children of the parent's active span.
            var traceParent = Environment.GetEnvironmentVariable("TRACEPARENT");
            var traceState = Environment.GetEnvironmentVariable("TRACESTATE");
            ActivityContext parentContext = default;
            if (traceParent is not null)
                ActivityContext.TryParse(traceParent, traceState, isRemote: true, out parentContext);

            using var rootActivity = MigrationPlatformActivitySources.WorkItemExport
                .StartActivity("TfsExport", ActivityKind.Server, parentContext);

            // Capture the full classification tree (area + iteration nodes) before the work item export.
            // This produces Nodes/source-tree.json in the package for use during import.
            var artefactStore = GetRequiredService<IArtefactStore>();
            var treeReader = GetRequiredService<IClassificationTreeReader>();
            var tfsEndpoint = new TeamFoundationServerEndpointOptions
            {
                Url = settings.CollectionUrl,
                Project = settings.Project,
                Type = "TeamFoundationServer"
            };
            await CaptureClassificationTreeAsync(treeReader, tfsEndpoint, artefactStore, cancellationToken)
                .ConfigureAwait(false);

            var agent = new TfsExportAgent(
                GetRequiredService<IArtefactStore>(),
                GetRequiredService<ICheckpointingService>(),
                GetRequiredService<IWorkItemRevisionSource>(),
                GetRequiredService<IAttachmentBinarySource>());

            // When stdout is redirected (subprocess mode) use NDJSON sink so the
            // parent process can parse progress events.  Otherwise render visually.
            if (Console.IsOutputRedirected)
            {
                var sink = new StdoutProgressSink();
                await agent.RunAsync(sink, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await AnsiConsole.Status()
                    .StartAsync("Exporting Work Items...", async ctx =>
                    {
                        ctx.Spinner(Spinner.Known.Dots);
                        ctx.SpinnerStyle(Style.Parse("green"));

                        await agent.RunAsync(
                            new DelegateProgressSink(evt =>
                            {
                                ctx.Status(
                                    "Exporting Work Items\n" +
                                    "[bold yellow]Stage:[/] " + (evt.Stage ?? "").PadRight(12) +
                                    "  [bold yellow]Module:[/] " + (evt.Module ?? "").PadRight(12) + "\n" +
                                    "[bold yellow]Status:[/] " + (evt.Message ?? "working…"));
                            }),
                            cancellationToken).ConfigureAwait(false);
                    });

                AnsiConsole.MarkupLine("[green]\u2705 Export complete.[/]");
                AnsiConsole.MarkupLineInterpolated($"Package written to [blue]{Path.GetFullPath(settings.OutputFolder)}[/]");
            }

            return 0;
        }

        private static readonly JsonSerializerOptions s_treeJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        /// <summary>
        /// Captures the full classification tree from TFS and writes Nodes/source-tree.json.
        /// This mirrors the ClassificationTreeCapture behaviour used by the ADO agent path.
        /// </summary>
        private static async Task CaptureClassificationTreeAsync(
            IClassificationTreeReader reader,
            MigrationEndpointOptions endpoint,
            IArtefactStore artefactStore,
            CancellationToken ct)
        {
            var areaNodes = new List<string>();
            var iterationNodes = new List<IterationNodeEntry>();

            await foreach (var path in reader.EnumerateAreaNodesAsync(endpoint, ct).ConfigureAwait(false))
                areaNodes.Add(path);

            await foreach (var entry in reader.EnumerateIterationNodesAsync(endpoint, ct).ConfigureAwait(false))
                iterationNodes.Add(entry);

            var snapshot = new { areaNodes, iterationNodes };
            var json = JsonSerializer.Serialize(snapshot, s_treeJsonOptions);
            await artefactStore.WriteAsync("Nodes/source-tree.json", json, ct).ConfigureAwait(false);
        }
    }
}

