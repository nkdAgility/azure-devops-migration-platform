using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Base class for commands that contact the control plane.
/// Adds <see cref="CreateHost(string[], string?, Action{IServiceCollection, IConfiguration}?)"/>
/// which resolves the control plane URL and, when none is configured, starts
/// <see cref="LocalStackHost"/> in-process before building the command host.
/// </summary>
/// <typeparam name="TSettings">Must be or derive from <see cref="ControlPlaneBaseCommandSettings"/>.</typeparam>
public abstract class ControlPlaneCommandBase<TSettings> : CommandBase<TSettings>
    where TSettings : ControlPlaneBaseCommandSettings
{
    private LocalStackHost? _localStack;

    /// <summary>
    /// Creates an <see cref="IHost"/> wired to the specified control plane URL.
    /// When <paramref name="controlPlaneUrl"/> is <c>null</c>, starts the local in-process
    /// stack (<see cref="LocalStackHost"/>) before building the host.
    /// </summary>
    protected async Task<IHost> CreateHost(
        string[] args,
        string? controlPlaneUrl,
        Action<IServiceCollection, IConfiguration>? configureServices = null)
    {
        if (Host is not null)
            return Host;

        if (controlPlaneUrl is null)
        {
            _localStack = new LocalStackHost();
            await _localStack.StartAsync();
            controlPlaneUrl = "http://localhost:5100";
        }

        Host = MigrationPlatformHost.CreateDefaultBuilder(args, configureServices, controlPlaneUrl).Build();
        await Host.StartAsync();
        return Host;
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
}
