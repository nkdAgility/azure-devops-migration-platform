using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Registers Azure DevOps work item export services for the IoC container.
/// </summary>
public static class ExportServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Azure DevOps work item export services:
    /// <list type="bullet">
    ///   <item><see cref="IAzureDevOpsWorkItemRevisionMapper"/> — maps REST revisions to the package model.</item>
    ///   <item><see cref="AzureDevOpsAttachmentRegistry"/> — per-export-run attachment URL store (scoped).</item>
    /// </list>
    /// The caller is responsible for:
    /// <list type="bullet">
    ///   <item>
    ///     Registering <see cref="IWorkItemRevisionSource"/> — typically
    ///     <see cref="AzureDevOpsWorkItemRevisionSource"/> constructed from the current migration context.
    ///   </item>
    ///   <item>
    ///     Registering <see cref="IAttachmentBinarySource"/> — typically
    ///     <see cref="AzureDevOpsAttachmentBinarySource"/> with PAT and
    ///     <see cref="AzureDevOpsAttachmentRegistry"/> injected.
    ///   </item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItemExport(
        this IServiceCollection services)
    {
        services.AddSingleton<IAzureDevOpsWorkItemRevisionMapper, AzureDevOpsWorkItemRevisionMapper>();
        services.AddScoped<AzureDevOpsAttachmentRegistry>();
        return services;
    }
}
