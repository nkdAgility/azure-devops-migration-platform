using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Export;
using DevOpsMigrationPlatform.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpectreValidationResult = Spectre.Console.ValidationResult;

namespace DevOpsMigrationPlatform.CLI.TfsExport.Commands
{
    public sealed class ExportCommand : AsyncCommand<ExportCommand.Settings>
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
                catch { return SpectreValidationResult.Error("--output path is not valid"); }

                return SpectreValidationResult.Success();
            }
        }

        public override Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {
            var configTable = new Table()
                .RoundedBorder()
                .BorderColor(Color.Grey)
                .AddColumn("[bold]Setting[/]")
                .AddColumn("[bold]Value[/]");

            configTable.AddRow("Collection", settings.CollectionUrl);
            configTable.AddRow("Project", settings.Project);
            configTable.AddRow("Output folder", settings.OutputFolder);

            AnsiConsole.Write(new Panel(configTable)
                .Header("[bold green]Export Configuration[/]")
                .Padding(1, 1)
                .BorderColor(Color.Green));

            throw new NotImplementedException(
                "IWorkItemRevisionSource for the TFS Object Model has not been implemented yet. " +
                "See docs/tfs-exporter.md for the required streaming, paging, and chunking contract.");
        }
    }
}

