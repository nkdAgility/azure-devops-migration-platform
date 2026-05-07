// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Net.Http;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Discovery;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Attachments;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
    ///   <item><see cref="IExportProgressStoreFactory"/> as <see cref="ExportProgressStoreFactory"/> — SQLite-backed per-work-item export progress for fast-forward resume.</item>
    ///   <item><see cref="AzureDevOpsAttachmentRegistry"/> — per-export-run attachment URL store (scoped).</item>
    ///   <item><see cref="IWorkItemCommentSourceFactory"/> as <see cref="AzureDevOpsWorkItemCommentSourceFactory"/> — creates comment sources per job (used for inline comment fetching when the Comments extension is enabled).</item>
    ///   <item><see cref="IEmbeddedImageDownloader"/> as <see cref="AzureDevOpsEmbeddedImageDownloader"/> — downloads embedded images with Polly resilience.</item>
    ///   <item><see cref="IEmbeddedImageExportService"/> as <see cref="EmbeddedImageExportService"/> — processes HTML/Markdown for image URLs and rewrites them.</item>
    /// </list>

    /// </summary>
    public static IServiceCollection AddAzureDevOpsWorkItemExport(
        this IServiceCollection services)
    {
        services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
        services.AddSingleton<IWiqlQueryClientFactory, AzureDevOpsWiqlQueryClientFactory>();
        services.AddSingleton<IWorkItemQueryWindowStrategy, WorkItemQueryWindowStrategy>();
        services.AddSingleton<IAzureDevOpsWorkItemRevisionMapper, AzureDevOpsWorkItemRevisionMapper>();
        services.AddSingleton<IExportProgressStoreFactory, ExportProgressStoreFactory>();
        services.AddScoped<AzureDevOpsAttachmentRegistry>();
        services.AddScoped<AzureDevOpsWorkItemRevisionSourceFactory>();
        services.AddRevisionSourceFactory<AzureDevOpsWorkItemRevisionSourceFactory>("AzureDevOpsServices");

        // Register ADO endpoint option types for polymorphic JSON deserialization
        services.AddEndpointOptionsType("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));
        services.AddOrganisationEntryType("AzureDevOpsServices", typeof(AzureDevOpsOrganisationEntry));

        // Inline comment fetching: factories registered; activated when the Comments extension is enabled.
        services.AddSingleton<IWorkItemCommentSourceFactory, AzureDevOpsWorkItemCommentSourceFactory>();

        // Work item discovery (count by WIQL query) — used by WorkItemExportOrchestrator
        // at job start to populate TotalWorkItems and emit a ScopeResolved event.
        // Registered here so the Agent has it available without the CLI needing it.
        services.AddWorkItemDiscoveryService<AzureDevOpsWorkItemDiscoveryService>("AzureDevOpsServices");

        // Classification tree reader — enumerates area/iteration nodes from the source ADO project.
        services.AddClassificationTreeReader<AzureDevOpsClassificationTreeReader>("AzureDevOpsServices");

        // Identity source — Azure DevOps identity enumeration keyed by connector type.
        services.AddIdentitySource<AzureDevOpsIdentitySource>("AzureDevOpsServices");

        // Team source — Azure DevOps Teams REST API keyed by connector type.
        services.AddTeamSource<AzureDevOpsTeamSource>("AzureDevOpsServices");

        // Team target — Azure DevOps Teams REST API keyed by connector type.
        services.AddTeamTarget<AzureDevOpsTeamTarget>("AzureDevOpsServices");

        // Embedded image download and processing
        services.AddHttpClient<AzureDevOpsEmbeddedImageDownloader>()
            .AddPolicyHandler(GetRetryPolicy());
        services.AddScoped<IEmbeddedImageDownloader>(
            sp => sp.GetRequiredService<AzureDevOpsEmbeddedImageDownloader>());
        // Note: IEmbeddedImageExportService is created on-demand by WorkItemExportOrchestrator,
        // not registered here, because it requires IArtefactStore which is only available at export time.

        // Attachment binary download: named HTTP client with resilience (8 retries, exponential back-off,
        // transient 5xx/408/429). AzureDevOpsAttachmentBinarySource uses this named client.
        // Note: IAttachmentBinarySource is not registered directly here because the access token is only
        // available at job execution time. The source is constructed by the revision source factory
        // and passed to WorkItemsModule/orchestrator at runtime.
        services.AddHttpClient("AttachmentDownload")
            .AddPolicyHandler(GetAttachmentRetryPolicy());

        services.AddAzureDevOpsEndpointInfo();

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
    /// Builds a Polly retry policy for attachment downloads — more aggressive than the default
    /// because attachment binaries are large and subject to throttling (429), intermittent 5xx,
    /// and request timeouts (408). Uses 8 retries with exponential back-off.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetAttachmentRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx, 408
            .OrResult(r => (int)r.StatusCode == 429) // Too Many Requests
            .Or<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 8,
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

        if (!services.Any(x => x.ServiceType == typeof(IWorkItemFetchService)))
        {
            services.AddSingleton<IWorkItemFetchService, AzureDevOpsWorkItemFetchService>();
        }

        services.AddSingleton<IWorkItemDiscoveryService, AzureDevOpsWorkItemDiscoveryService>();

        // Register ADO endpoint option types for polymorphic JSON deserialization
        services.AddEndpointOptionsType("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));
        services.AddOrganisationEntryType("AzureDevOpsServices", typeof(AzureDevOpsOrganisationEntry));

        return services;
    }

    /// <summary>
    /// Registers <see cref="ISourceEndpointInfo"/> and <see cref="ITargetEndpointInfo"/> for the AzureDevOps connector.
    /// Reads values from <see cref="IOptions{AzureDevOpsEndpointOptions}"/>.
    /// Uses TryAddSingleton so the dynamic active-job implementations registered
    /// by the MigrationAgent take precedence when both connectors are in the same host.
    /// </summary>
    private static IServiceCollection AddAzureDevOpsEndpointInfo(this IServiceCollection services)
    {
        // Source endpoint info — TryAdd so dynamic per-job impl takes precedence
        services.TryAddSingleton<ISourceEndpointInfo>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureDevOpsEndpointOptions>>().Value;
            return new AzureDevOpsSourceEndpointInfo
            {
                Url = opts.GetResolvedUrl(),
                Project = opts.GetProject(),
                ConnectorType = "AzureDevOpsServices"
            };
        });

        // Target endpoint info — TryAdd so dynamic per-job impl takes precedence
        services.TryAddSingleton<ITargetEndpointInfo>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureDevOpsEndpointOptions>>().Value;
            return new AzureDevOpsTargetEndpointInfo
            {
                Url = opts.GetResolvedUrl(),
                Project = opts.GetProject(),
                ConnectorType = "AzureDevOpsServices"
            };
        });

        return services;
    }

    /// <summary>
    /// Inline implementation of <see cref="ISourceEndpointInfo"/> for AzureDevOps connector.
    /// </summary>
    private sealed record AzureDevOpsSourceEndpointInfo : ISourceEndpointInfo
    {
        public required string Url { get; init; }
        public required string Project { get; init; }
        public required string ConnectorType { get; init; }
    }

    /// <summary>
    /// Inline implementation of <see cref="ITargetEndpointInfo"/> for AzureDevOps connector.
    /// </summary>
    private sealed record AzureDevOpsTargetEndpointInfo : ITargetEndpointInfo
    {
        public required string Url { get; init; }
        public required string Project { get; init; }
        public required string ConnectorType { get; init; }
    }
}
