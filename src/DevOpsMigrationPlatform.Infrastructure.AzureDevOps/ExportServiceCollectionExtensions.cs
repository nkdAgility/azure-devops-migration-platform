using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Polly;
using Polly.Extensions.Http;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Registers Azure DevOps work item export services for the IoC container.
/// </summary>
public static class ExportServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Azure DevOps work item export services:
    /// <list type="bullet">
    ///   <item><see cref="IAzureDevOpsClientFactory"/> — creates Azure DevOps HTTP clients.</item>
    ///   <item><see cref="IAzureDevOpsWorkItemRevisionMapper"/> — maps REST revisions to the package model.</item>
    ///   <item><see cref="IWorkItemRevisionSourceFactory"/> as <see cref="AzureDevOpsWorkItemRevisionSourceFactory"/> — constructs revision sources per job.</item>
    ///   <item><see cref="AzureDevOpsAttachmentRegistry"/> — per-export-run attachment URL store (scoped).</item>
    ///   <item><see cref="IWorkItemCommentSourceFactory"/> as <see cref="AzureDevOpsWorkItemCommentSourceFactory"/> — creates comment sources per job (used for inline comment fetching when the Comments extension is enabled).</item>
    ///   <item><see cref="IEmbeddedImageDownloader"/> as <see cref="AzureDevOpsEmbeddedImageDownloader"/> — downloads embedded images with Polly resilience.</item>
    ///   <item><see cref="IEmbeddedImageExportService"/> as <see cref="EmbeddedImageExportService"/> — processes HTML/Markdown for image URLs and rewrites them.</item>
    ///   <item><see cref="WorkItemsModule"/> — the <see cref="IModule"/> implementation.</item>
    /// </list>

    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItemExport(
        this IServiceCollection services)
    {
        services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        services.AddSingleton<IWiqlQueryClientFactory, AzureDevOpsWiqlQueryClientFactory>();
        services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>();
        services.AddSingleton<IAzureDevOpsWorkItemRevisionMapper, AzureDevOpsWorkItemRevisionMapper>();
        services.AddScoped<AzureDevOpsAttachmentRegistry>();
        services.AddScoped<IWorkItemRevisionSourceFactory, AzureDevOpsWorkItemRevisionSourceFactory>();

        // Inline comment fetching: factories registered; activated when the Comments extension is enabled.
        services.AddSingleton<IWorkItemCommentSourceFactory, AzureDevOpsWorkItemCommentSourceFactory>();

        // Embedded image download and processing
        services.AddHttpClient<AzureDevOpsEmbeddedImageDownloader>()
            .AddPolicyHandler(GetRetryPolicy());
        services.AddScoped<IEmbeddedImageDownloader>(
            sp => sp.GetRequiredService<AzureDevOpsEmbeddedImageDownloader>());
        // Note: IEmbeddedImageExportService is created on-demand by WorkItemExportOrchestrator,
        // not registered here, because it requires IArtefactStore which is only available at export time.

        services.AddTransient<IModule, WorkItemsModule>();
        return services;
    }

    /// <summary>
    /// Builds a Polly retry policy for HTTP 429, 5xx, and timeouts.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx, 408
            .Or<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    // Logging happens at the service level
                });
    }

    /// <summary>
    /// Registers only the services required for pre-flight work item counting.
    /// Lighter-weight than <see cref="AddAzureDevOpsWorkItemExport"/> — registers the
    /// query window strategy and discovery service without the full export pipeline.
    /// Intended for CLI commands that need a work item count before submitting a job.
    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItemCount(
        this IServiceCollection services)
    {
        services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        services.AddSingleton<IWiqlQueryClientFactory, AzureDevOpsWiqlQueryClientFactory>();
        services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>();
        services.AddSingleton<IWorkItemDiscoveryService, AzureDevOpsWorkItemDiscoveryService>();
        return services;
    }
}
