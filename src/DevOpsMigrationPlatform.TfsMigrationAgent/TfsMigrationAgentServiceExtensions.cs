using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;

namespace DevOpsMigrationPlatform.TfsMigrationAgent;

/// <summary>
/// Registers all TFS Migration Agent services into the host's DI container.
/// Structural twin of <c>MigrationAgentServiceExtensions</c> but targets net481
/// and uses <see cref="IServiceCollection"/> directly (no <c>IHostApplicationBuilder</c>
/// on net481).
/// </summary>
public static class TfsMigrationAgentServiceExtensions
{
    public static IServiceCollection AddTfsMigrationAgentServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Uri controlPlaneBaseUrl)
    {
        // Core shared services — ambient state, telemetry, HTTP clients, progress sinks,
        // store factories, diagnostics, and the telemetry push timer.
        // No Polly resilience handler on net481 — simple retry in the polling loop is sufficient
        // for localhost communication.
        services.AddCoreAgentServices(configuration, controlPlaneBaseUrl);

        // Package config store — reads migration-config.json from the package at job pickup.
        services.AddPackageConfigStore();

        // Per-job TFS Object Model service factory — creates TFS connections, revision sources,
        // attachment sources, tree readers, and discovery services per job based on the endpoint.
        services.AddSingleton<ITfsJobServiceFactory, TfsJobServiceFactory>();

        // Ambient state carrying the current job's TFS services (set by TfsJobAgentWorker before running modules).
        services.AddSingleton<ActiveTfsJobServices>();

        // TFS adapter implementations for module contracts.
        services.AddSingleton<IClassificationTreeCapture, TfsClassificationTreeCapture>();
        services.AddSingleton<IWorkItemRevisionSourceFactory, TfsActiveJobWorkItemRevisionSourceFactory>();
        services.AddSingleton<IIdentitySource, TfsActiveJobIdentitySource>();

        // Export progress store — SQLite-backed fast-forward resume (now supported on net481).
        services.AddSingleton<IExportProgressStoreFactory, ExportProgressStoreFactory>();

        // Register IModule pipeline (export-only on net481).
        services.AddIdentitiesModule(configuration);
        services.AddNodesModule(configuration);
        services.AddWorkItemsModule();

        // TFS source endpoint info — reads from ActiveTfsJobServices (source-only, no target).
        services.AddTfsSourceEndpointInfo();

        // Unified worker — polls /agents/lease?capabilities=tfs and dispatches to TFS execution.
        services.AddSingleton<IHostedService, TfsJobAgentWorker>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="ISourceEndpointInfo"/> for the TFS connector (source-only).
    /// Reads values from <see cref="ActiveTfsJobServices"/>, which is populated by
    /// <see cref="TfsJobAgentWorker"/> when a job is picked up.
    /// </summary>
    private static IServiceCollection AddTfsSourceEndpointInfo(this IServiceCollection services)
    {
        services.TryAddSingleton<ISourceEndpointInfo>(sp =>
        {
            var activeServices = sp.GetRequiredService<ActiveTfsJobServices>();
            var endpoint = activeServices.Require().Endpoint;
            return new TfsSourceEndpointInfo(
                Url: endpoint.GetResolvedUrl(),
                Project: endpoint.GetProject(),
                ConnectorType: "TeamFoundationServer"
            );
        });

        return services;
    }

    /// <summary>
    /// Inline implementation of <see cref="ISourceEndpointInfo"/> for TFS connector.
    /// </summary>
    private sealed record TfsSourceEndpointInfo(string Url, string Project, string ConnectorType) : ISourceEndpointInfo
    {
        // TFS uses its own SDK for auth — return a minimal endpoint for compatibility.
        public OrganisationEndpoint ToOrganisationEndpoint() =>
            new OrganisationEndpoint { ResolvedUrl = Url, Type = ConnectorType };
    }
}
