// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Testing;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryInventory;

/// <summary>
/// Fluent builder for Discovery Inventory tests.
/// Supports both in-process (TestConsole + mock IInventoryService) and
/// out-of-process (CliRunner) execution paths.
/// Dispose via <c>await using</c> to clean up the isolated working directory.
/// </summary>
public sealed class DiscoveryInventoryBuilder : IAsyncDisposable
{
    private readonly string _isolatedWorkingDirectory;
    private readonly List<InventoryProjectSetup> _projects = new();
    private bool _zeroProjects;
    private bool _sequential;
    private bool _invalidPat;

    public DiscoveryInventoryBuilder()
    {
        _isolatedWorkingDirectory = Path.Combine(
            Path.GetTempPath(),
            "discovery-inventory-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_isolatedWorkingDirectory);
    }

    // ── Capability: Live Table Rendering ─────────────────────────────────────

    /// <summary>
    /// Registers named projects whose inventory events will be streamed
    /// through a controlled mock <see cref="IInventoryService"/>.
    /// Each project yields two events: one intermediate and one final.
    /// </summary>
    public DiscoveryInventoryBuilder WithProjects(params string[] projectNames)
    {
        foreach (var name in projectNames)
            _projects.Add(CreateProjectSetup(name));
        return this;
    }

    /// <summary>
    /// Registers a multi-project controlled stream where events are released
    /// one project at a time, allowing ordering assertions.
    /// Projects are counted sequentially: all events for project N complete
    /// before the first event for project N+1 is released.
    /// </summary>
    public DiscoveryInventoryBuilder WithSequentialProjects(params string[] projectNames)
    {
        _sequential = true;
        foreach (var name in projectNames)
            _projects.Add(CreateProjectSetup(name));
        return this;
    }

    private static InventoryProjectSetup CreateProjectSetup(string projectName) =>
        new()
        {
            ProjectName = projectName,
            WorkItemsCount = 5,
            RevisionsCount = 10,
            ReposCount = 2,
            PipelinesCount = 3,
            LastUpdatedUtc = new DateTime(2024, 1, 1, 14, 30, 45, DateTimeKind.Utc),
        };

    // ── Capability: Empty Organisation Handling ───────────────────────────────

    /// <summary>
    /// Configures the mock <see cref="IInventoryService"/> to yield no events,
    /// simulating an Azure DevOps organisation with zero projects.
    /// </summary>
    public DiscoveryInventoryBuilder WithZeroProjects()
    {
        _zeroProjects = true;
        return this;
    }

    // ── Capability: Authentication Failure Handling ───────────────────────────

    /// <summary>
    /// Configures the out-of-process invocation to use an invalid PAT value,
    /// triggering an authentication failure response from the CLI.
    /// </summary>
    public DiscoveryInventoryBuilder WithInvalidPat()
    {
        _invalidPat = true;
        return this;
    }

    // ── Act ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the <c>discovery inventory</c> command in-process via a
    /// <see cref="Spectre.Console.Testing.TestConsole"/> and a mock
    /// <see cref="IInventoryService"/>. Captures rendered output, exit code,
    /// and any files written to the isolated working directory.
    /// Use for rendering, CSV output, and sequential counting scenarios.
    /// </summary>
    public async Task<DiscoveryInventoryResult> RunInProcessAsync()
    {
        var testConsole = new TestConsole();
        var mockService = BuildMockInventoryService();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IInventoryService>(mockService);
                services.AddSingleton<IAnsiConsole>(testConsole);
            })
            .Build();

        var command = new InventoryCommand();
        command.Host = host;

        var settings = new InventoryCommandSettings();
        var remaining = new Mock<IRemainingArguments>();
        remaining.Setup(r => r.Raw).Returns(Array.Empty<string>());
        remaining.Setup(r => r.Parsed).Returns(Enumerable.Empty<string>().ToLookup(x => x, x => (string?)x));
        var cmdCtx = new CommandContext(Array.Empty<string>(), remaining.Object, "inventory", null);

        // Override working directory so CSV lands in the isolated temp folder.
        var previousEnv = Environment.GetEnvironmentVariable("DEVOPS_MIGRATION_WORKING_DIR");
        Environment.SetEnvironmentVariable("DEVOPS_MIGRATION_WORKING_DIR", _isolatedWorkingDirectory);

        int exitCode;
        try
        {
            var executeMethod = typeof(InventoryCommand).GetMethod(
                "ExecuteInternalAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            exitCode = await (Task<int>)executeMethod.Invoke(
                command,
                new object[] { cmdCtx, settings, CancellationToken.None })!;
        }
        finally
        {
            if (previousEnv is null)
                Environment.SetEnvironmentVariable("DEVOPS_MIGRATION_WORKING_DIR", null);
            else
                Environment.SetEnvironmentVariable("DEVOPS_MIGRATION_WORKING_DIR", previousEnv);
        }

        var output = testConsole.Output;

        return new DiscoveryInventoryResult(
            exitCode: exitCode,
            renderedOutput: output,
            standardError: string.Empty,
            timedOut: false,
            isolatedWorkingDirectory: _isolatedWorkingDirectory,
            disposeAsync: DisposeAsync);
    }

    /// <summary>
    /// Executes the <c>discovery inventory</c> command as a subprocess via
    /// <see cref="CliRunner"/>. Captures exit code, stdout, and stderr.
    /// Use for authentication failure scenarios where the process must exit
    /// with a non-zero code from within the live CLI binary.
    /// </summary>
    public async Task<DiscoveryInventoryResult> RunOutOfProcessAsync()
    {
        var args = new List<string> { "discovery", "inventory" };

        if (_invalidPat)
        {
            args.AddRange(["--organisation", "https://dev.azure.com/testorg", "--token", "invalid-pat-value"]);
        }

        var env = new Dictionary<string, string>
        {
            ["DEVOPS_MIGRATION_WORKING_DIR"] = _isolatedWorkingDirectory,
        };

        var cliResult = await CliRunner.RunAsync(args, env: env, timeout: TimeSpan.FromMinutes(2));

        return new DiscoveryInventoryResult(
            exitCode: cliResult.ExitCode,
            renderedOutput: cliResult.StandardOutput,
            standardError: cliResult.StandardError,
            timedOut: cliResult.TimedOut,
            isolatedWorkingDirectory: _isolatedWorkingDirectory,
            disposeAsync: DisposeAsync);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_isolatedWorkingDirectory))
                Directory.Delete(_isolatedWorkingDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
        return ValueTask.CompletedTask;
    }

    // ── Private: mock builder ─────────────────────────────────────────────────

    private IInventoryService BuildMockInventoryService()
    {
        var mock = new Mock<IInventoryService>();

        if (_zeroProjects)
        {
            mock.Setup(s => s.RunInventoryAsync(
                    It.IsAny<HashSet<string>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(AsyncEnumerable.Empty<InventoryProgressEvent>());
        }
        else if (_sequential)
        {
            var allEvents = BuildProjectEvents(_projects);
            mock.Setup(s => s.RunInventoryAsync(
                    It.IsAny<HashSet<string>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<HashSet<string>?, CancellationToken>((_, ct) =>
                    YieldSequentially(allEvents, ct));
        }
        else
        {
            var allEvents = BuildProjectEvents(_projects);
            mock.Setup(s => s.RunInventoryAsync(
                    It.IsAny<HashSet<string>?>(),
                    It.IsAny<CancellationToken>()))
                .Returns<HashSet<string>?, CancellationToken>((_, ct) =>
                    YieldSequentially(allEvents, ct));
        }

        return mock.Object;
    }

    private static List<InventoryProgressEvent> BuildProjectEvents(List<InventoryProjectSetup> projects)
    {
        var events = new List<InventoryProgressEvent>();
        foreach (var p in projects)
        {
            // Intermediate event
            events.Add(new InventoryProgressEvent
            {
                ProjectName = p.ProjectName,
                WorkItemsCount = p.WorkItemsCount / 2,
                RevisionsCount = p.RevisionsCount / 2,
                ReposCount = 0,
                PipelinesCount = 0,
                IsComplete = false,
                Timestamp = p.LastUpdatedUtc,
            });
            // Final event
            events.Add(new InventoryProgressEvent
            {
                ProjectName = p.ProjectName,
                WorkItemsCount = p.WorkItemsCount,
                RevisionsCount = p.RevisionsCount,
                ReposCount = p.ReposCount,
                PipelinesCount = p.PipelinesCount,
                IsComplete = true,
                Timestamp = p.LastUpdatedUtc,
            });
        }
        return events;
    }

    private static async IAsyncEnumerable<InventoryProgressEvent> YieldSequentially(
        List<InventoryProgressEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return evt;
        }
    }
}
