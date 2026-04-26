using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
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
    /// <see cref="INodeCreator"/> and <see cref="IClassificationTreeReader"/> are
    /// connector-specific and must be registered by the connector DI
    /// (e.g. <c>AddAzureDevOpsWorkItemImport</c>, <c>AddSimulatedWorkItemImport</c>,
    /// <c>TfsClassificationTreeReader</c>).
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
#endif

        return services;
    }
}
