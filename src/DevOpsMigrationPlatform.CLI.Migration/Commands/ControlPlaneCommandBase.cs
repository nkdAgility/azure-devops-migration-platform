using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Base class for commands that contact the control plane.
/// Resolves the control plane URL from <see cref="EnvironmentOptions"/> (bound from config).
/// When <see cref="EnvironmentType.Standalone"/>, starts <see cref="LocalStackHost"/> in-process.
/// When <see cref="EnvironmentType.Hosted"/>, verifies the external control plane is reachable
/// before proceeding with expensive operations.
///
/// The <c>--port</c> setting (from <see cref="ControlPlaneBaseCommandSettings.Port"/>) overrides
/// the <c>ControlPlane.BaseUrl</c> to <c>http://localhost:{port}</c> in standalone mode, enabling
/// multiple concurrent local runs on different ports.
/// </summary>
/// <typeparam name="TSettings">Must be or derive from <see cref="ControlPlaneBaseCommandSettings"/>.</typeparam>
public abstract class ControlPlaneCommandBase<TSettings> : CommandBase<TSettings>
    where TSettings : ControlPlaneBaseCommandSettings
{
    private LocalStackHost? _localStack;

    /// <summary>
    /// The standalone port captured from settings before host creation.
    /// </summary>
    private int _standalonePort = 5100;

    /// <summary>
    /// When <c>true</c> (the default), the command starts <see cref="LocalStackHost"/> in-process
    /// when the environment type is <see cref="EnvironmentType.Standalone"/>.
    /// Observer-only commands (e.g. <c>tui</c>) that connect to an already-running control plane
    /// must override this to <c>false</c>.
    /// </summary>
    protected virtual bool StartsLocalStack => true;

    /// <summary>
    /// Captures the <c>--port</c> setting before the command lifecycle begins,
    /// then delegates to the standard <see cref="CommandBase{TSettings}"/> template method.
    /// </summary>
    protected override async Task<int> ExecuteAsync(
        CommandContext context, TSettings settings, CancellationToken cancellationToken = default)
    {
        _standalonePort = settings.Port;
        return await base.ExecuteAsync(context, settings, cancellationToken);
    }

    /// <summary>
    /// Creates an <see cref="IHost"/> wired to the control plane URL from
    /// <see cref="EnvironmentOptions"/>. When the <c>--port</c> flag differs from the config
    /// default, injects an in-memory configuration override so the control plane URL matches
    /// the requested port. When the environment type is <see cref="EnvironmentType.Standalone"/>
    /// and <see cref="StartsLocalStack"/> is <c>true</c>, starts the local in-process stack.
    /// When the environment type is <see cref="EnvironmentType.Hosted"/>, verifies the external
    /// control plane is reachable.
    /// </summary>
    protected new async Task<IHost> CreateHost(
        string[] args,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
    {
        if (Host is not null)
            return Host;

        var builder = MigrationPlatformHost.CreateDefaultBuilder(GetEffectiveArgs(args), configureServices);

        // When --port overrides the default, inject an in-memory config source so that
        // EnvironmentOptions.ControlPlane.BaseUrl resolves to the requested port.
        // In-memory sources added last take highest priority.
        if (_standalonePort != 5100)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{EnvironmentOptions.SectionName}:ControlPlane:BaseUrl"] = $"http://localhost:{_standalonePort}"
                });
            });
        }

        Host = builder.Build();

        var envOpts = Host.Services.GetRequiredService<IOptions<EnvironmentOptions>>().Value;

        if (StartsLocalStack && envOpts.Type == EnvironmentType.Standalone)
        {
            var uri = new Uri(envOpts.ControlPlane.BaseUrl);
            _localStack = new LocalStackHost(uri.Port);
            await _localStack.StartAsync();
        }
        else if (envOpts.Type == EnvironmentType.Hosted)
        {
            await VerifyControlPlaneReachableAsync(envOpts.ControlPlane.BaseUrl);
        }

        await Host.StartAsync();
        return Host;
    }

    /// <summary>
    /// Verifies that the external control plane is reachable in <see cref="EnvironmentType.Hosted"/> mode.
    /// Fails fast with an actionable error if the control plane cannot be reached,
    /// preventing expensive preflight operations (e.g. work item counting) from running first.
    /// </summary>
    private static async Task VerifyControlPlaneReachableAsync(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var response = await http.GetAsync("/jobs");
            // Any HTTP response (even 4xx) means the service is running.
        }
        catch (HttpRequestException ex)
        {
            throw new MigrationException(
                $"Control plane at {baseUrl} is not reachable. " +
                "If you want to run locally, set '\"Environment\": {{ \"Type\": \"Standalone\" }}' " +
                "in your config (or remove the Environment section) to start a local control plane automatically.",
                MigrationErrorCategory.Transient,
                isRetryable: false,
                guidance: "In Hosted mode, the control plane must already be running. " +
                          "Start it with 'devopsmigration controlplane start', or switch to Standalone mode.",
                innerException: ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new MigrationException(
                $"Control plane at {baseUrl} did not respond within 5 seconds. " +
                "The control plane may be starting up — try again, or check that it is running.",
                MigrationErrorCategory.Transient,
                isRetryable: true,
                guidance: "If you want to run locally, set '\"Environment\": {{ \"Type\": \"Standalone\" }}' in your config.",
                innerException: ex);
        }
    }

    /// <summary>
    /// Returns the resolved control plane base URL from <see cref="EnvironmentOptions"/>.
    /// </summary>
    protected string GetControlPlaneUrl()
    {
        var envOpts = Host!.Services.GetRequiredService<IOptions<EnvironmentOptions>>().Value;
        return envOpts.ControlPlane.BaseUrl;
    }

    /// <inheritdoc/>
    protected override async Task DisposeResourcesAsync()
    {
        if (_localStack is not null)
        {
            await _localStack.DisposeAsync();
            _localStack = null;
        }
    }

    /// <summary>
    /// Prints the assigned Job ID and control plane URL immediately after a successful job submission.
    /// Must be called before any progress output begins (FR-012, FR-013, SC-004).
    /// </summary>
    protected static void PrintJobSubmitted(IAnsiConsole console, Guid jobId, string controlPlaneUrl)
    {
        console.MarkupLine("[green]\u2713[/] Job submitted.");
        console.MarkupLine($"  Job ID  : [bold]{jobId}[/]");
        console.MarkupLine($"  Control : [blue]{Markup.Escape(controlPlaneUrl)}[/]");
    }
}
