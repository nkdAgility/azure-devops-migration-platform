using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;
using System;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Base class for commands that contact the control plane.
/// Resolves the control plane URL from <see cref="EnvironmentOptions"/> (bound from config).
/// When <see cref="EnvironmentType.Standalone"/>, starts <see cref="LocalStackHost"/> in-process.
/// </summary>
/// <typeparam name="TSettings">Must be or derive from <see cref="ControlPlaneBaseCommandSettings"/>.</typeparam>
public abstract class ControlPlaneCommandBase<TSettings> : CommandBase<TSettings>
    where TSettings : ControlPlaneBaseCommandSettings
{
    private LocalStackHost? _localStack;

    /// <summary>
    /// When <c>true</c> (the default), the command starts <see cref="LocalStackHost"/> in-process
    /// when the environment type is <see cref="EnvironmentType.Standalone"/>.
    /// Observer-only commands (e.g. <c>tui</c>) that connect to an already-running control plane
    /// must override this to <c>false</c>.
    /// </summary>
    protected virtual bool StartsLocalStack => true;

    /// <summary>
    /// Creates an <see cref="IHost"/> wired to the control plane URL from
    /// <see cref="EnvironmentOptions"/>. When the environment type is
    /// <see cref="EnvironmentType.Standalone"/> and <see cref="StartsLocalStack"/> is <c>true</c>,
    /// starts the local in-process stack first.
    /// </summary>
    protected new async Task<IHost> CreateHost(
        string[] args,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
    {
        if (Host is not null)
            return Host;

        Host = MigrationPlatformHost.CreateDefaultBuilder(GetEffectiveArgs(args), configureServices).Build();

        var envOpts = Host.Services.GetRequiredService<IOptions<EnvironmentOptions>>().Value;

        if (StartsLocalStack && envOpts.Type == EnvironmentType.Standalone)
        {
            _localStack = new LocalStackHost();
            await _localStack.StartAsync();
        }

        await Host.StartAsync();
        return Host;
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
