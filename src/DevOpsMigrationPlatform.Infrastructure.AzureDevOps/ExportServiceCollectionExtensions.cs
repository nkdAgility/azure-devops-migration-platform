using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using DevOpsMigrationPlatform.Infrastructure.Modules;
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
    ///   <item><see cref="IAzureDevOpsClientFactory"/> — creates ADO HTTP clients.</item>
    ///   <item><see cref="IAzureDevOpsWorkItemRevisionMapper"/> — maps REST revisions to the package model.</item>
    ///   <item><see cref="IWorkItemRevisionSourceFactory"/> as <see cref="AzureDevOpsWorkItemRevisionSourceFactory"/> — constructs revision sources per job.</item>
    ///   <item><see cref="AzureDevOpsAttachmentRegistry"/> — per-export-run attachment URL store (scoped).</item>
    ///   <item><see cref="WorkItemsModule"/> — the <see cref="IDataTypeModule"/> implementation.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItemExport(
        this IServiceCollection services)
    {
        services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        services.AddSingleton<IAzureDevOpsWorkItemRevisionMapper, AzureDevOpsWorkItemRevisionMapper>();
        services.AddScoped<AzureDevOpsAttachmentRegistry>();
        services.AddScoped<IWorkItemRevisionSourceFactory, AzureDevOpsWorkItemRevisionSourceFactory>();
        services.AddTransient<IDataTypeModule, WorkItemsModule>();
        return services;
    }
}
