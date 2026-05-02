// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using DevOpsMigrationPlatform.Infrastructure.Config;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
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
                .AddMeter(WellKnownMeterNames.Migration)
                .AddMeter(WellKnownMeterNames.Discovery));

        // Core shared services — ambient state, telemetry, HTTP clients, progress sinks,
        // store factories, diagnostics, and the telemetry push timer.
        // The /agents/lease endpoint is a long-poll held open by the server for ~10s.
        // Increase timeouts to prevent Polly from firing spurious timeouts on every idle poll.
        // Polly validation constraints:
        //   TotalRequestTimeout >= AttemptTimeout * (MaxRetries + 1)  →  150s >= 30s * 4 = 120s ✓
        //   CircuitBreaker.SamplingDuration >= 2 * AttemptTimeout     →  61s  >= 2 * 30s = 60s  ✓
        builder.Services.AddCoreAgentServices(builder.Configuration, controlPlaneBaseUrl,
            httpBuilder => httpBuilder.AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(150);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(61);
            }));

        // Register flat IOptions<T> bindings for top-level config sections.
        // These are read-only per-host bindings; per-job re-reads go through IOptionsSnapshot<T>
        // with ActiveJobConfigState.PackageConfig (see tool DI extensions).
        builder.Services.AddOptions<MigrationPackageOptions>()
            .BindConfiguration(MigrationPackageOptions.SectionName);
        builder.Services.AddSchemaEntry<MigrationPackageOptions>("Package storage and path configuration");
        builder.Services.AddOptions<MigrationPoliciesOptions>()
            .BindConfiguration(MigrationPoliciesOptions.SectionName);
        builder.Services.AddSchemaEntry<MigrationPoliciesOptions>("Retry, throttle, and checkpoint policy configuration");

        // Register cross-cutting tool services (NodeTranslation + FieldTransform).
        builder.Services.AddNodeTranslationToolServices();

        // Register dynamic endpoint info — reads ConnectorType/Url/Project from
        // ActiveJobConfigState.Current on every access, so every job picks the
        // correct connector regardless of which connectors are registered.
        // Must be registered BEFORE any connector calls AddSingleton/TryAddSingleton
        // so connectors never override these with a static per-connector value.
        builder.Services.TryAddSingleton<ISourceEndpointInfo, ActiveJobSourceEndpointInfo>();
        builder.Services.TryAddSingleton<ITargetEndpointInfo, ActiveJobTargetEndpointInfo>();
        builder.Services.TryAddSingleton<IAgentJobContext, ActiveJobAgentJobContext>();
        builder.Services.AddFieldTransformToolServices();

        // Package config store — reads migration-config.json from the package at job pickup.
        builder.Services.AddPackageConfigStore();

        // Register IModule implementations.
        builder.Services.AddIdentitiesModule(builder.Configuration);
        builder.Services.AddNodesModule(builder.Configuration);
        builder.Services.AddTeamsModule(builder.Configuration);
        builder.Services.AddAzureDevOpsWorkItemExport();
        builder.Services.AddAzureDevOpsWorkItemImport();
        builder.Services.AddWorkItemsModule();

        // Simulated connector — required for offline tests and CI scenarios.
        builder.Services.AddSimulatedServices();

        // Register the polymorphic endpoint converter so JobAgentWorker can deserialise
        // Source/Target from migration-config.json using System.Text.Json.
        // Must be called AFTER all AddEndpointOptionsType registrations so the registry
        // collects all connector types when it is first resolved.
        builder.Services.AddMigrationPlatformPolymorphicSerializers();

        // Register discovery modules (Inventory, Dependencies) as IModule implementations.
        // They run as part of the Export plan when needed, not via a separate DiscoveryAgentWorker.
        // ICatalogService must be registered before AddAzureDevOpsDependencyAnalysis.
        builder.Services.AddSingleton<ICatalogService, CatalogService>();
        builder.Services.AddAzureDevOpsInventory(builder.Configuration);
        builder.Services.AddAzureDevOpsDependencyAnalysis(builder.Configuration);
        builder.Services.AddInventoryModule();
        builder.Services.AddDependenciesModule();

        // Unified worker — polls /agents/lease and dispatches to migration or discovery execution.
        builder.Services.AddHostedService<JobAgentWorker>();

        return builder;
    }
}
