using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        return services;
    }

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
