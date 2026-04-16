using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.ControlPlane;

/// <summary>
/// Starts the bundled Control Plane host process in the current terminal.
///
/// Resolves <c>ControlPlane/DevOpsMigrationPlatform.ControlPlaneHost[.exe]</c>
/// relative to the CLI binary location — the layout that the distributable zip
/// produces. When running from a dev/source build this directory does not exist
/// and the command prints an informative error instead of crashing.
///
/// Usage: <c>devopsmigration controlplane start</c>
/// </summary>
public sealed class ControlPlaneStartCommand : AsyncCommand<ControlPlaneStartCommand.Settings>
{
    /// <summary>No settings required — the binary is resolved by convention.</summary>
    public sealed class Settings : CommandSettings { }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken = default)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "DevOpsMigrationPlatform.ControlPlaneHost.exe"
            : "DevOpsMigrationPlatform.ControlPlaneHost";

        var exePath = Path.Combine(
            AppContext.BaseDirectory,
            "ControlPlane",
            exeName);

        if (!File.Exists(exePath))
        {
            AnsiConsole.MarkupLine("[red]✗ Control Plane binary not found.[/]");
            AnsiConsole.MarkupLine($"  Expected: [dim]{Markup.Escape(exePath)}[/]");
            AnsiConsole.MarkupLine("[grey]This command is only available in the packaged (zip) distribution.[/]");
            AnsiConsole.MarkupLine("[grey]When running from a source build, start the Control Plane directly:[/]");
            AnsiConsole.MarkupLine("[grey]  dotnet run --project src/DevOpsMigrationPlatform.ControlPlaneHost[/]");
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the Control Plane process.");

        AnsiConsole.MarkupLine($"[green]✓[/] Control Plane started (PID {process.Id})  [dim]http://localhost:5100[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");

        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* already exited */ }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
