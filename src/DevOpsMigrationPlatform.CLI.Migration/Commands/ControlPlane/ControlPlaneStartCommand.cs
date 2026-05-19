// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
/// Usage: <c>devopsmigration controlplane start [--url &lt;baseUrl&gt;]</c>
/// </summary>
public sealed class ControlPlaneStartCommand : AsyncCommand<ControlPlaneStartCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--url")]
        [Description("Control Plane base URL. Default: http://localhost:5100")]
        public string? Url { get; init; }

        [CommandOption("--port")]
        [Description("Legacy alias for the URL port. Ignored when --url is provided. Default: 5100.")]
        [DefaultValue(5100)]
        public int Port { get; init; } = 5100;
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveBaseUrl(settings.Url, settings.Port, out var baseUrl, out var validationError))
        {
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(validationError!)}[/]");
            return 1;
        }
        var resolvedBaseUrl = baseUrl!;

        var exePath = ChildProcessHost.ResolveExecutablePath("ControlPlane", "DevOpsMigrationPlatform.ControlPlaneHost");

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            AnsiConsole.MarkupLine("[red]✗ Control Plane binary not found.[/]");
            AnsiConsole.MarkupLine("[grey]Expected one of:[/]");
            AnsiConsole.MarkupLine("[grey]  - Installed layout: ControlPlane/DevOpsMigrationPlatform.ControlPlaneHost[/]");
            AnsiConsole.MarkupLine("[grey]  - Dev build output under src/DevOpsMigrationPlatform.ControlPlaneHost/bin[/]");
            AnsiConsole.MarkupLine("[grey]When running from a source build, start the Control Plane directly:[/]");
            AnsiConsole.MarkupLine($"[grey]  dotnet run --project src/DevOpsMigrationPlatform.ControlPlaneHost --urls {Markup.Escape(resolvedBaseUrl)}[/]");
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
        psi.Environment["ASPNETCORE_URLS"] = resolvedBaseUrl;
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["AgentLifecycle__AutoSpawn"] = "true";
        psi.Environment["MigrationPlatform__Environment__Type"] = "Standalone";
        psi.Environment["MigrationPlatform__Environment__ControlPlane__BaseUrl"] = resolvedBaseUrl;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the Control Plane process.");

        AnsiConsole.MarkupLine($"[green]✓[/] Control Plane started (PID {process.Id})  [dim]{Markup.Escape(resolvedBaseUrl)}[/]");
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

    private static bool TryResolveBaseUrl(string? rawUrl, int port, out string? baseUrl, out string? error)
    {
        baseUrl = null;
        error = null;

        var candidate = string.IsNullOrWhiteSpace(rawUrl)
            ? $"http://localhost:{port}"
            : rawUrl.Trim();

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed))
        {
            error = "Invalid URL format. Provide an absolute URL, for example: http://localhost:5100";
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Unsupported URL scheme. Only http and https are supported.";
            return false;
        }

        baseUrl = parsed.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}
