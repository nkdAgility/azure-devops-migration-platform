using Spectre.Console;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Views;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>
/// Internal TFS export helper invoked by <see cref="AzureDevOpsExportCommand"/> when
/// <c>config.Source.Type == "TeamFoundationServer"</c>.
///
/// This is NOT a Spectre.Console command — it is not registered in Program.cs.
/// From the user's perspective, <c>devopsmigration export</c> handles both Azure DevOps Services
/// and TFS/Azure DevOps Server transparently by inspecting <c>Source.Type</c>.
///
/// See docs/tfs-exporter.md for the full subprocess protocol and
/// system-architecture guardrail rule 19.
/// </summary>
internal static class TfsExportRunner
{
    /// <summary>
    /// Launches the TFS exporter subprocess and streams its output via the progress pipeline.
    /// </summary>
    /// <param name="config">Fully loaded and validated <see cref="MigrationOptions"/>.</param>
    /// <param name="serviceProvider">The command's DI container — must have <see cref="TfsExporterProcessAdapter"/>, <see cref="IExternalToolRunner"/>, and <see cref="IProgressSink"/>.</param>
    /// <param name="tfsExportExePathOverride">Optional path override for tfsmigration.exe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<int> RunAsync(
        MigrationOptions config,
        IServiceProvider serviceProvider,
        string? tfsExportExePathOverride,
        CancellationToken cancellationToken)
    {
        var exePath = tfsExportExePathOverride ?? ResolveExePath();

        if (!File.Exists(exePath))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] tfsmigration.exe not found at: {exePath}");
            AnsiConsole.MarkupLine("[grey]Build the DevOpsMigrationPlatform.CLI.TfsMigration project first, or set a scenario config with an explicit exe path override.[/]");
            return 1;
        }

        var collectionUrl = config.Source!.Url;
        var project = config.Source.Project;
        var outputFolder = Path.GetFullPath(config.Artefacts.ExpandedPath);

        var arguments = $"export" +
                        $" --collection \"{collectionUrl}\"" +
                        $" --project \"{project}\"" +
                        $" --output \"{outputFolder}\"";

        AnsiConsole.MarkupLineInterpolated($"[grey]Launching:[/] {exePath}");

        var panel = new TelemetryPanel();
        var adapter = serviceProvider.GetRequiredService<TfsExporterProcessAdapter>();
        var runner = serviceProvider.GetRequiredService<IExternalToolRunner>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var exitCode = await runner.RunWithStreamingAsync(
            exePath,
            arguments,
            onOutput: line =>
            {
                adapter.OnStdoutLine(line, cts.Token);
                panel.Render(AnsiConsole.Console);
            },
            onError: line => AnsiConsole.MarkupLineInterpolated($"[red]{line}[/]"));

        if (exitCode != 0)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]✗[/] TFS export failed with exit code {exitCode}");
            return exitCode;
        }

        AnsiConsole.MarkupLine("[green]✓[/] TFS export complete.");
        AnsiConsole.MarkupLineInterpolated($"Package written to [blue]{outputFolder}[/]");
        return 0;
    }

    /// <summary>
    /// Resolves tfsmigration.exe relative to this assembly.
    /// In a published single-folder layout both CLIs live side-by-side.
    /// In a Debug build we traverse the typical output structure.
    /// </summary>
    internal static string ResolveExePath()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(TfsExportRunner).Assembly.Location) ?? ".";

        // Side-by-side (published layout)
        var sideBySide = Path.Combine(assemblyDir, "tfsmigration.exe");
        if (File.Exists(sideBySide)) return sideBySide;

        // Debug layout: navigate from CLI.Migration bin up to CLI.TfsMigration bin
        return Path.GetFullPath(
            Path.Combine(assemblyDir,
                @"..\..\..\..\DevOpsMigrationPlatform.CLI.TfsMigration\bin\Debug\net481\tfsmigration.exe"));
    }
}

