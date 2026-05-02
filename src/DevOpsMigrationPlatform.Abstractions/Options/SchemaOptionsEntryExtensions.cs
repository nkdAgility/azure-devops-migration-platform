#if NET7_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Convenience extension for registering a schema entry alongside IOptions&lt;T&gt;.
/// </summary>
public static class SchemaOptionsEntryExtensions
{
    /// <summary>
    /// Registers a <see cref="SchemaOptionsEntry"/> for the given options type.
    /// The generic constraint enforces that T implements IConfigSection, ensuring
    /// SectionName exists at compile time without reflection.
    /// Idempotent — safe to call multiple times for the same type.
    /// </summary>
    /// <typeparam name="T">Options type implementing IConfigSection</typeparam>
    /// <param name="services">Service collection</param>
    /// <param name="description">Optional description for schema documentation</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddSchemaEntry<T>(
        this IServiceCollection services,
        string? description = null)
        where T : class, IConfigSection
    {
        // Check if an entry for this OptionsType already exists
        var alreadyRegistered = services.Any(descriptor =>
            descriptor.ServiceType == typeof(SchemaOptionsEntry) &&
            descriptor.ImplementationInstance is SchemaOptionsEntry entry &&
            entry.OptionsType == typeof(T));

        if (!alreadyRegistered)
        {
            services.AddSingleton(new SchemaOptionsEntry
            {
                OptionsType = typeof(T),
                SectionPath = T.SectionName,
                Description = description
            });
        }
        
        return services;
    }
}
#endif
