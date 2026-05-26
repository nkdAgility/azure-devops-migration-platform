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

namespace DevOpsMigrationPlatform.CLI.Commands.Agent;

/// <summary>
/// Starts the bundled Migration Agent process in the current terminal.
///
/// Resolves the agent executable via <see cref="ChildProcessHost.ResolveExecutablePath"/>
/// so it works in installed and development layouts.
///
/// Usage: <c>devopsmigration agent start [--url &lt;baseUrl&gt;]</c>
/// </summary>
public sealed class AgentStartCommand : AsyncCommand<AgentStartCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--url")]
        [Description("Control Plane base URL the agent will connect to. Default: http://localhost:5100")]
        public string? Url { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveBaseUrl(settings.Url, out var baseUrl, out var validationError))
        {
            AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(validationError!)}[/]");
            return 1;
        }
        var resolvedBaseUrl = baseUrl!;

        var exePath = ChildProcessHost.ResolveExecutablePath("MigrationAgent", "DevOpsMigrationPlatform.MigrationAgent");
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            AnsiConsole.MarkupLine("[red]✗ Migration Agent binary not found.[/]");
            AnsiConsole.MarkupLine("[grey]Expected one of:[/]");
            AnsiConsole.MarkupLine("[grey]  - Installed layout: MigrationAgent/DevOpsMigrationPlatform.MigrationAgent[/]");
            AnsiConsole.MarkupLine("[grey]  - Dev build output under src/DevOpsMigrationPlatform.MigrationAgent/bin[/]");
            AnsiConsole.MarkupLine("[grey]When running from a source build, start the agent directly:[/]");
            AnsiConsole.MarkupLine($"[grey]  dotnet run --project src/DevOpsMigrationPlatform.MigrationAgent -- --ControlPlane:BaseUrl={Markup.Escape(resolvedBaseUrl)}[/]");
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

        psi.ArgumentList.Add($"--ControlPlane:BaseUrl={resolvedBaseUrl}");
        psi.Environment["ControlPlane__BaseUrl"] = resolvedBaseUrl;
        psi.Environment["MigrationPlatform__Environment__Type"] = "Standalone";
        psi.Environment["MigrationPlatform__Environment__ControlPlane__BaseUrl"] = resolvedBaseUrl;
        psi.Environment["DOTNET_ENVIRONMENT"] = "Production";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment["Logging__LogLevel__Default"] = "Information";
        psi.Environment["Logging__LogLevel__DevOpsMigrationPlatform"] = "Information";
        psi.Environment["Logging__Console__LogLevel__Default"] = "Information";
        psi.Environment["Telemetry__DetailedDiagnostics"] = "true";

        var diagnosticsPath = Environment.GetEnvironmentVariable("Telemetry__DiagnosticsPath");
        if (string.IsNullOrWhiteSpace(diagnosticsPath))
        {
            diagnosticsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".otel-diagnostics"));
            Environment.SetEnvironmentVariable("Telemetry__DiagnosticsPath", diagnosticsPath);
        }
        psi.Environment["Telemetry__DiagnosticsPath"] = diagnosticsPath;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the Migration Agent process.");

        AnsiConsole.MarkupLine($"[green]✓[/] Migration Agent started (PID {process.Id})  [dim]{Markup.Escape(resolvedBaseUrl)}[/]");
        AnsiConsole.MarkupLine($"[blue]ℹ[/] AgentExe: [dim]{Markup.Escape(exePath)}[/]");
        AnsiConsole.MarkupLine($"[blue]ℹ[/] DiagnosticsPath: [dim]{Markup.Escape(diagnosticsPath)}[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");

        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
        });

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static bool TryResolveBaseUrl(string? rawUrl, out string? baseUrl, out string? error)
    {
        baseUrl = null;
        error = null;

        var candidate = string.IsNullOrWhiteSpace(rawUrl)
            ? "http://localhost:5100"
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
