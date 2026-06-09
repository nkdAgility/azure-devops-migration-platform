// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobList;

/// <summary>
/// Simulates the TuiCommand error path (health check before Application.Init) by
/// reproducing the minimal logic: call GetAllJobsAsync, catch network failure, write
/// the error message to a captured <see cref="IAnsiConsole"/>.
/// </summary>
public sealed class TuiCommandErrorHarness
{
    private readonly StringBuilder _output = new();

    /// <summary>Captured <see cref="IAnsiConsole"/> backed by an in-memory <see cref="StringWriter"/>.</summary>
    public IAnsiConsole Console { get; }

    /// <summary>The URL that will be reported in error messages.</summary>
    public string EffectiveUrl { get; set; } = "http://localhost:5100";

    public TuiCommandErrorHarness()
    {
        Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(new StringWriter(_output)),
            ColorSystem = (ColorSystemSupport)ColorSystem.NoColors,
            Ansi = AnsiSupport.No,
        });
    }

    /// <summary>
    /// Executes the health-check block against <paramref name="client"/>.
    /// Returns 1 if the client throws <see cref="HttpRequestException"/> or
    /// <see cref="TaskCanceledException"/>, 0 on success.
    /// </summary>
    public async Task<int> RunHealthCheckAsync(IControlPlaneClient client, CancellationToken ct = default)
    {
        try
        {
            await client.GetAllJobsAsync(ct).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.MarkupLine($"[red]✗ Cannot reach Control Plane at {Markup.Escape(EffectiveUrl)}[/]");
            Console.MarkupLine("[grey]Ensure the control plane is running and the Environment.ControlPlane.BaseUrl config is correct.[/]");
            return 1;
        }
    }

    /// <summary>Returns the captured console output as plain text (markup stripped).</summary>
    public string CapturedOutput => _output.ToString();
}
