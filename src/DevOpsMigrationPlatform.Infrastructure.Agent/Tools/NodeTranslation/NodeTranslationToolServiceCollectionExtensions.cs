using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// DI registration for the NodeTranslation tool and its supporting services.
/// </summary>
public static class NodeTranslationToolServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="INodeTranslationTool"/>, <see cref="INodeTranslationTool"/>, and <see cref="INodeTranslationValidator"/>
    /// to the service collection.
    /// <para>
    /// <see cref="INodeCreator"/> and <see cref="IClassificationTreeReader"/> are
    /// connector-specific and must be registered by the connector DI
    /// (e.g. <c>AddAzureDevOpsWorkItemImport</c>, <c>AddSimulatedWorkItemImport</c>,
    /// <c>TfsClassificationTreeReader</c>).
    /// </para>
    /// </summary>
    public static IServiceCollection AddNodeTranslationToolServices(this IServiceCollection services)
    {
        services.AddOptions<NodeTranslationOptions>()
            .BindConfiguration(NodeTranslationOptions.SectionName)
            .ValidateOnStart();

#if !NET481
        services.AddSingleton<IValidateOptions<NodeTranslationOptions>, NodeTranslationOptionsValidator>();
        services.AddSingleton<NodeTranslationTool>();
        services.AddSingleton<INodeTranslationTool>(sp => sp.GetRequiredService<NodeTranslationTool>());
        services.AddSingleton<INodeTranslationTool>(sp => sp.GetRequiredService<NodeTranslationTool>());
        services.AddSingleton<INodeTranslationValidator, NodeTranslationValidator>();
        // T012: ReferencedPathTracker must be Singleton so the same path set is shared across a single export run
        services.AddSingleton<ReferencedPathTracker>();
        services.AddSingleton<IReferencedPathTracker>(sp => sp.GetRequiredService<ReferencedPathTracker>());
        services.AddSingleton<ClassificationTreeCapture>();
        services.AddSingleton<IClassificationTreeCapture>(sp => sp.GetRequiredService<ClassificationTreeCapture>());
        services.AddSingleton<NodeEnsurer>();
        services.AddSingleton<INodeEnsurer>(sp => sp.GetRequiredService<NodeEnsurer>());
#endif

        return services;
    }
}
