using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;

/// <summary>
/// DI registration for the field-transform tool and its supporting services.
/// </summary>
public static class FieldTransformToolServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IFieldTransformFactory"/>, <see cref="IFieldTransformTool"/>,
    /// and <see cref="IFieldTransformValidator"/> to the service collection.
    /// </summary>
    public static IServiceCollection AddFieldTransformToolServices(this IServiceCollection services)
    {
        services.AddOptions<FieldTransformOptions>()
            .BindConfiguration(FieldTransformOptions.SectionName)
            .ValidateOnStart();

#if !NET481
        services.AddSingleton<IValidateOptions<FieldTransformOptions>, FieldTransformOptionsValidator>();
#endif

        services.AddSingleton<IFieldTransformFactory>(sp =>
            new FieldTransformFactory(sp.GetService<ILoggerFactory>()));
        services.AddSingleton<IFieldTransformTool, FieldTransformTool>();

        // IFieldDefinitionProviderFactory is optional — connectors register it when available.
        services.AddSingleton<IFieldTransformValidator>(sp =>
            new FieldTransformValidator(
                sp.GetRequiredService<IOptions<FieldTransformOptions>>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<FieldTransformValidator>(),
                sp.GetService<IFieldDefinitionProviderFactory>()));

        return services;
    }
}
