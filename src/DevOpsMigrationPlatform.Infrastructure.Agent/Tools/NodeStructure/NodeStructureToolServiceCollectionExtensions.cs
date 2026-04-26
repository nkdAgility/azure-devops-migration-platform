using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;

/// <summary>
/// DI registration for the NodeStructure tool and its supporting services.
/// </summary>
public static class NodeStructureToolServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="INodeStructureTool"/> and <see cref="INodeStructureValidator"/>
    /// to the service collection.
    /// <para>
    /// <see cref="INodeCreator"/> defaults to <see cref="AzureDevOpsNodeCreator"/>
    /// (a no-op placeholder). Connector-specific DI may override it via
    /// <see cref="ServiceCollectionDescriptorExtensions.Replace"/>.
    /// </para>
    /// <para>
    /// <see cref="IClassificationTreeReader"/> is connector-specific and must be
    /// registered by the connector DI.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNodeStructureToolServices(this IServiceCollection services)
    {
        services.AddOptions<NodeStructureOptions>()
            .BindConfiguration(NodeStructureOptions.SectionName)
            .ValidateOnStart();

#if !NET481
        services.AddSingleton<IValidateOptions<NodeStructureOptions>, NodeStructureOptionsValidator>();
        services.AddSingleton<INodeStructureTool, NodeStructureTool>();
        services.AddSingleton<INodeStructureValidator, NodeStructureValidator>();
        services.AddTransient<ReferencedPathTracker>();
        services.AddTransient<ClassificationTreeCapture>();

        // Default INodeCreator — no-op placeholder; connectors replace with a real HTTP implementation.
        services.TryAddSingleton<INodeCreator, AzureDevOpsNodeCreator>();
#endif

        return services;
    }
}
