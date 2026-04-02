using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>
/// Launches the tfsexport.exe subprocess (net481) and streams its output.
/// The executable is resolved relative to this assembly so that both Debug
/// and published-layout paths work automatically.
/// </summary>
public sealed class TfsExportCommand : AsyncCommand<TfsExportCommand.Settings>
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

        [CommandOption("--tfsexport-path <PATH>")]
        [Description("Override path to tfsexport.exe (resolved automatically by default)")]
        public string? TfsExportExePath { get; set; }

        public override Spectre.Console.ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(CollectionUrl))
                return Spectre.Console.ValidationResult.Error("--collection is required");
            if (string.IsNullOrWhiteSpace(Project))
                return Spectre.Console.ValidationResult.Error("--project is required");
            try { Path.GetFullPath(OutputFolder); }
            catch { return Spectre.Console.ValidationResult.Error("--output path is not valid"); }
            return Spectre.Console.ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var exePath = settings.TfsExportExePath ?? ResolveExePath();

        if (!File.Exists(exePath))
        {
            AnsiConsole.MarkupLineInterpolated($"❌ [red]tfsmigration.exe not found at:[/] {exePath}");
            AnsiConsole.MarkupLine("[grey]Use --tfsexport-path to specify its location,[/]");
            AnsiConsole.MarkupLine("[grey]or build the DevOpsMigrationPlatform.CLI.TfsMigration project first.[/]");
            return -1;
        }

        var outputFolder = Path.GetFullPath(settings.OutputFolder);
        var arguments = $"export" +
                        $" --collection \"{settings.CollectionUrl}\"" +
                        $" --project \"{settings.Project}\"" +
                        $" --output \"{outputFolder}\"";

        AnsiConsole.MarkupLineInterpolated($"[grey]Launching:[/] {exePath}");
        AnsiConsole.MarkupLineInterpolated($"[grey]Arguments:[/] {arguments}");

        var exitCode = await ExternalToolRunner.RunWithStreamingAsync(
            exePath,
            arguments,
            onOutput: line => AnsiConsole.MarkupLineInterpolated($"[grey]{line}[/]"),
            onError: line => AnsiConsole.MarkupLineInterpolated($"[red]{line}[/]"));

        if (exitCode != 0)
        {
            AnsiConsole.MarkupLineInterpolated($"❌ [red]TFS export failed with exit code {exitCode}[/]");
            return exitCode;
        }

        AnsiConsole.MarkupLine("✅ [green]TFS export complete.[/]");
        AnsiConsole.MarkupLineInterpolated($"Package written to [blue]{outputFolder}[/]");
        return 0;
    }

    /// <summary>
    /// Resolves tfsexport.exe relative to this assembly.
    /// In a published single-folder layout both CLIs live side-by-side.
    /// In a Debug build we traverse the typical output structure.
    /// </summary>
    private static string ResolveExePath()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(TfsExportCommand).Assembly.Location) ?? ".";

        // Side-by-side (published layout)
        var sideBySide = Path.Combine(assemblyDir, "tfsmigration.exe");
        if (File.Exists(sideBySide)) return sideBySide;

        // Debug layout: navigate from CLI.Migration bin up to CLI.TfsMigration bin
        var debugRelative = Path.GetFullPath(
            Path.Combine(assemblyDir,
                @"..\..\..\..\DevOpsMigrationPlatform.CLI.TfsMigration\bin\Debug\net481\tfsmigration.exe"));
        return debugRelative;
    }
}
