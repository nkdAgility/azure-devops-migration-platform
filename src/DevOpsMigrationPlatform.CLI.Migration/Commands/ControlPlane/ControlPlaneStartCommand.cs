using System;
using System.ComponentModel;
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
/// The port is passed to the child process via the <c>ASPNETCORE_URLS</c>
/// environment variable — the standard ASP.NET Core override mechanism.
///
/// Usage: <c>devopsmigration controlplane start [--port &lt;port&gt;]</c>
/// </summary>
public sealed class ControlPlaneStartCommand : AsyncCommand<ControlPlaneStartCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--port")]
        [Description("Port the Control Plane host will listen on. Default: 5100.")]
        [DefaultValue(5100)]
        public int Port { get; init; } = 5100;
    }

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
            AnsiConsole.MarkupLine($"[grey]  dotnet run --project src/DevOpsMigrationPlatform.ControlPlaneHost --urls http://localhost:{settings.Port}[/]");
            return 1;
        }

        var url = $"http://localhost:{settings.Port}";

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        psi.Environment["ASPNETCORE_URLS"] = url;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the Control Plane process.");

        AnsiConsole.MarkupLine($"[green]✓[/] Control Plane started (PID {process.Id})  [dim]{Markup.Escape(url)}[/]");
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
