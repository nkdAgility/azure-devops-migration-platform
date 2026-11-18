// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Dependencies;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Inventory;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using DevOpsMigrationPlatform.Infrastructure.Config;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// Registers all Migration Agent services into a host's DI container.
/// Called from <c>Program.cs</c> (standalone agent) and from <c>LocalStackHost</c>
/// (CLI in-process local stack). Both paths use the exact same registrations.
/// </summary>
public static class MigrationAgentServiceExtensions
{
    /// <summary>
    /// Registers all services required by <see cref="MigrationAgentWorker"/> and its
    /// dependencies. Does not call <c>AddServiceDefaults()</c> — that is Aspire-specific
    /// and must only be called from the standalone <c>Program.cs</c>.
    /// </summary>
    /// <param name="builder">The application host builder.</param>
    /// <param name="controlPlaneBaseUrl">The base URL of the running ControlPlane API.</param>
    public static IHostApplicationBuilder AddMigrationAgentServices(
        this IHostApplicationBuilder builder,
        Uri controlPlaneBaseUrl)
    {
        // Register WellKnownMeterNames meters in the OTel pipeline.
        // Use ConfigureOpenTelemetryMeterProvider (the pattern recommended by the
        // Azure Monitor docs) so the subscription runs on the same MeterProvider
        // that UseAzureMonitor() exports from.
        builder.Services.ConfigureOpenTelemetryMeterProvider((sp, mb) => mb
                .AddMeter(WellKnownMeterNames.Agent));

        // Core shared services — ambient state, telemetry, HTTP clients, progress sinks,
        // store factories, diagnostics, and the telemetry push timer.
        // The /agents/lease endpoint is a long-poll held open by the server for ~10s.
        // Increase timeouts to prevent Polly from firing spurious timeouts on every idle poll.
        // Polly validation constraints:
        //   TotalRequestTimeout >= AttemptTimeout * (MaxRetries + 1)  →  200s >= 45s * 4 = 180s ✓
        //   CircuitBreaker.SamplingDuration >= 2 * AttemptTimeout     →  91s  >= 2 * 45s = 90s  ✓
        // RemoveAllResilienceHandlers first: AddServiceDefaults puts the standard handler (10 s
        // attempt timeout) on every client, and it would cut each lease long-poll short (#171).
        // EXTEXP0001: RemoveAllResilienceHandlers is the documented way to replace that default.
#pragma warning disable EXTEXP0001
        builder.Services.AddCoreAgentServices(builder.Configuration, controlPlaneBaseUrl,
            httpBuilder => httpBuilder.RemoveAllResilienceHandlers().AddStandardResilienceHandler(options =>
            {
                // Longer than the Control Plane's 30 s lease long poll, so an idle poll reaches
                // the 204 branch instead of timing out (PR #222 review).
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(45);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(200);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(91);
            }));
#pragma warning restore EXTEXP0001

        // Register flat IOptions<T> bindings for top-level config sections.
        // These are read-only per-host bindings; per-job re-reads go through IOptionsSnapshot<T>
        // with the explicit current package-config accessor (see tool DI extensions).
        builder.Services.AddOptions<MigrationPackageOptions>()
            .BindConfiguration(MigrationPackageOptions.SectionName);
        builder.Services.AddSchemaEntry<MigrationPackageOptions>("Package storage and path configuration");
        builder.Services.AddOptions<MigrationPoliciesOptions>()
            .BindConfiguration(MigrationPoliciesOptions.SectionName);
        builder.Services.AddSchemaEntry<MigrationPoliciesOptions>("Retry, throttle, and checkpoint policy configuration");

        // Register cross-cutting tool services (NodeTranslation + FieldTransform).
        builder.Services.AddNodeTranslationToolServices();

        // Register dynamic endpoint info — reads ConnectorType/Url/Project from
        // the current per-job endpoint accessor on every access, so every job picks the
        // correct connector regardless of which connectors are registered.
        // Must be registered BEFORE any connector calls AddSingleton/TryAddSingleton
        // so connectors never override these with a static per-connector value.
        builder.Services.TryAddSingleton<ISourceEndpointInfo, ActiveJobSourceEndpointInfo>();
        builder.Services.TryAddSingleton<ITargetEndpointInfo, ActiveJobTargetEndpointInfo>();
        builder.Services.TryAddSingleton<IAgentJobContext, ActiveJobAgentJobContext>();
        builder.Services.AddFieldTransformToolServices();

        // Package storage and config store — filesystem store must be registered before config loader.
        builder.Services.AddPackageStorageServices();
        builder.Services.AddPackageMigrationConfigLoader();

        // Register IModule implementations.
        builder.Services.AddIdentitiesModule(builder.Configuration);
        builder.Services.AddNodesModule(builder.Configuration);
        builder.Services.AddTeamsModule(builder.Configuration);
        builder.Services.AddAzureDevOpsWorkItemExport();
        builder.Services.AddAzureDevOpsWorkItem();
        builder.Services.AddWorkItemsModule(builder.Configuration);

        // Simulated connector — required for offline tests and CI scenarios.
        builder.Services.AddSimulatedServices();

        // Embedded-image download for the active job's source connector (#169, #154).
        builder.Services.AddScoped<DevOpsMigrationPlatform.Infrastructure.Agent.Export.SourceEndpointEmbeddedImageDownloader>();
        builder.Services.AddScoped<DevOpsMigrationPlatform.Abstractions.Agent.Attachments.IEmbeddedImageDownloader, SourceConnectorEmbeddedImageDownloader>();

        // Register the polymorphic endpoint converter so JobAgentWorker can deserialise
        // Source/Target from migration-config.json using System.Text.Json.
        // Must be called AFTER all AddEndpointOptionsType registrations so the registry
        // collects all connector types when it is first resolved.
        builder.Services.AddMigrationPlatformPolymorphicSerializers();

        // Register discovery services and analysers.
        // ICatalogService must be registered before AddAzureDevOpsDependencyAnalysis.
        builder.Services.AddSingleton<ICatalogService, CatalogService>();
        builder.Services.AddAzureDevOpsInventory(builder.Configuration);
        builder.Services.AddAzureDevOpsDependencyAnalysis(builder.Configuration);
        builder.Services.AddInventoryOrchestratorServices();
        builder.Services.AddInventoryAnalyserServices();
        builder.Services.AddDependencyAnalyserServices();
        builder.Services.AddDependencyCapture();

        // Unified worker — polls /agents/lease and dispatches to migration or discovery execution.
        builder.Services.AddHostedService<JobAgentWorker>();

        return builder;
    }
}
