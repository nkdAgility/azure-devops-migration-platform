using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
#if !NET481
using System;
using DevOpsMigrationPlatform.Infrastructure.Config;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Linq;
#endif

namespace DevOpsMigrationPlatform.Infrastructure;

/// <summary>
/// Extension methods for wiring up the platform and module options into a DI container.
///
/// Usage in a host's ConfigureServices:
/// <code>
/// services.AddMigrationPlatformOptions(configuration);
/// </code>
///
/// Usage in a module assembly's own ServiceCollectionExtensions:
/// <code>
/// services.AddModuleOptions&lt;WorkItemsModuleOptions&gt;(configuration, "WorkItems");
/// services.AddSingleton&lt;IModule, WorkItemsModule&gt;();
/// </code>
/// </summary>
public static class MigrationPlatformServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IOptions{MigrationOptions}"/> bound to the
    /// <c>MigrationPlatform</c> section of <paramref name="configuration"/>.
    /// Also registers the <see cref="MigrationOptionsValidator"/>.
    /// </summary>
    public static IServiceCollection AddMigrationPlatformOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MigrationOptions>, MigrationOptionsValidator>();

        services.AddOptions<MigrationOptions>()
            .Bind(configuration.GetSection("MigrationPlatform"))
            .ValidateOnStart();

#if !NET481
        // Polymorphic endpoint options registry — built from all EndpointOptionsRegistration entries
        services.AddMigrationPlatformPolymorphicSerializers();
#endif

        return services;
    }

#if !NET481
    /// <summary>
    /// Registers the polymorphic JSON serializers (registry + converters) without binding
    /// <see cref="MigrationOptions"/>. Call this when the full
    /// <see cref="AddMigrationPlatformOptions"/> is not available (e.g. the shared CLI host builder).
    /// Connector assemblies must register their own types with
    /// <see cref="AddEndpointOptionsType"/> before the registry singleton is first resolved.
    /// </summary>
    public static IServiceCollection AddMigrationPlatformPolymorphicSerializers(
        this IServiceCollection services)
    {
        services.TryAddSingleton<EndpointOptionsTypeRegistry>(sp =>
        {
            var registry = new EndpointOptionsTypeRegistry();
            foreach (var reg in sp.GetServices<EndpointOptionsRegistration>())
            {
                if (reg.IsOrganisationEntry)
                    registry.RegisterOrganisationEntry(reg.Key, reg.Type);
                else
                    registry.Register(reg.Key, reg.Type);
            }
            return registry;
        });
        services.TryAddSingleton<PolymorphicEndpointOptionsConverter>(sp =>
            new PolymorphicEndpointOptionsConverter(sp.GetRequiredService<EndpointOptionsTypeRegistry>()));
        services.TryAddSingleton<PolymorphicOrganisationEntryConverter>(sp =>
            new PolymorphicOrganisationEntryConverter(sp.GetRequiredService<EndpointOptionsTypeRegistry>()));
        return services;
    }

    /// <summary>
    /// Registers a <see cref="IPostConfigureOptions{DiscoveryOptions}"/> that polymorphically
    /// binds the <c>Organisations</c> array from <c>MigrationPlatform:Organisations</c>.
    /// Call after <c>AddOptions&lt;DiscoveryOptions&gt;().Bind(...)</c> — the post-configure
    /// replaces the empty list left by <see cref="Microsoft.Extensions.Configuration.ConfigurationBinder.Bind"/>
    /// (which cannot instantiate abstract <see cref="OrganisationEntry"/>).
    /// Idempotent — multiple calls register only one binder.
    /// Also ensures the polymorphic serializers (including <see cref="EndpointOptionsTypeRegistry"/>)
    /// are registered.
    /// </summary>
    public static IServiceCollection AddDiscoveryOptionsOrganisationsBinder(
        this IServiceCollection services)
    {
        services.AddMigrationPlatformPolymorphicSerializers();
        services.TryAddSingleton<IPostConfigureOptions<DiscoveryOptions>>(sp =>
            new DiscoveryOptionsOrganisationsBinder(
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<EndpointOptionsTypeRegistry>()));
        return services;
    }

    /// <summary>
    /// Registers <see cref="IPackageLockService"/> → <see cref="PackageLockFileService"/> as a
    /// singleton. Requires <see cref="IControlPlaneClient"/> to already be registered in the container
    /// (used for stale-lock liveness checks).
    /// </summary>
    /// <param name="agentInstanceId">
    /// Unique GUID identifying this agent process instance for the duration of its lifetime.
    /// Generated by <c>JobAgentWorker</c> on startup.
    /// </param>
    public static IServiceCollection AddPackageLockServices(
        this IServiceCollection services,
        Guid agentInstanceId)
    {
        services.AddSingleton<IPackageLockService>(sp =>
            new PackageLockFileService(
                agentInstanceId,
                sp.GetRequiredService<IControlPlaneClient>(),
                sp.GetRequiredService<ILogger<PackageLockFileService>>()));
        return services;
    }
#endif

    /// <summary>
    /// Registers <see cref="IOptions{TOptions}"/> for a module, bound to the
    /// <c>Modules:{moduleName}</c> section of <paramref name="configuration"/>.
    ///
    /// Module assemblies call this in their own service registration method, e.g.:
    /// <code>
    /// // In DevOpsMigrationPlatform.Module.WorkItems:
    /// services.AddModuleOptions&lt;WorkItemsModuleOptions&gt;(configuration, "WorkItems");
    /// </code>
    ///
    /// The module then injects <c>IOptions&lt;WorkItemsModuleOptions&gt;</c> to read its config.
    /// </summary>
    /// <typeparam name="TOptions">
    /// The module's options class.  Must implement <see cref="IModuleOptions"/>.
    /// </typeparam>
    /// <param name="moduleName">
    /// The module name exactly as it appears as a key under <c>Modules</c> in the
    /// config file, e.g. <c>"WorkItems"</c>.
    /// </param>
    public static OptionsBuilder<TOptions> AddModuleOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string moduleName)
        where TOptions : class, IModuleOptions
    {
        return services.AddOptions<TOptions>()
            .Bind(configuration.GetSection($"Modules:{moduleName}"))
            .ValidateOnStart();
    }
}
