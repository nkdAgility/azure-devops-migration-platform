// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

/// <summary>
/// Extension methods for registering teams services with the DI container.
/// </summary>
public static class TeamsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TeamsModule"/> as an <see cref="IModule"/> implementation
    /// and all supporting services (orchestrators, slug generator, options).
    /// </summary>
    public static IServiceCollection AddTeamsModule(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
#if NET7_0_OR_GREATER
        // Register schema entry for migration.schema.json generation
        services.AddSchemaEntry<TeamsModuleOptions>("Teams export/import module configuration");
        services.AddSchemaEntry<BoardConfigDataOptions>("Board configuration payload-carry toggles (Data aspect)");
        services.AddSchemaEntry<BoardConfigProcessingOptions>("Board configuration import behaviour (Processing aspect)");
#endif

        // Scoped (not Singleton) so each per-job DI scope gets its own TeamsOrchestrator
        // instance and — via TeamsOrchestrator → TeamMigrationOrchestrator — its own
        // IReferencedPathLifecycle.  The T012 invariant requires every component within a
        // single job scope to share the same ReferencedPathLifecycle so the internal
        // SemaphoreSlim correctly serialises concurrent file writes to
        // Nodes/referenced-paths.json.  A Singleton TeamsOrchestrator would capture the
        // root-scope IReferencedPathLifecycle (a different instance from the per-job one
        // used by WorkItemsModule), breaking that coordination and causing a sharing-
        // violation IOException under concurrent export.
        services.AddScoped<ITeamsOrchestrator, TeamsOrchestrator>();
        services.AddTransient<IModule, TeamsModule>();

        if (configuration is not null)
        {
            services.Configure<TeamsModuleOptions>(
                configuration.GetSection(TeamsModuleOptions.SectionName));
        }
        else
        {
            services.AddOptions<TeamsModuleOptions>();
        }

        services.AddTransient<TeamMigrationOrchestrator>();
        services.AddSingleton<TeamSlugGenerator>();

        // BoardConfig extension — split options per the module-anatomy contract (ADR-0028
        // amendment): payload-carry toggles are Data, import behaviour is Processing.
        services.AddOptions<BoardConfigDataOptions>()
            .BindConfiguration(BoardConfigDataOptions.SectionName);
        services.AddOptions<BoardConfigProcessingOptions>()
            .BindConfiguration(BoardConfigProcessingOptions.SectionName);
        // Canonical board-config merge/validation engine (ADR-0024, EC-M4).
        services.AddSingleton<DevOpsMigrationPlatform.Abstractions.Agent.Tools.IBoardConfigMergeTool, BoardConfigMergeTool>();
        services.AddSingleton<BoardConfigTeamExtension>();
        services.AddSingleton<IModuleExtension>(sp => sp.GetRequiredService<BoardConfigTeamExtension>());

        // Team capability extensions (IModuleExtension ports, ADR-0024/EC-H1) — iterations,
        // members, capacity, and area paths. Scoped, not Singleton: the iterations extension
        // shares the per-job scoped IReferencedPathLifecycle (see the TeamsOrchestrator
        // lifetime note above); a Singleton would capture the root-scope instance.
        //
        // ITeamTarget falls back to the CompositeTeamTarget dispatcher so source-only hosts
        // (the net481 TFS agent registers no connector ITeamTarget) can still resolve the
        // export extensions — the composite resolves a concrete target only when an import
        // method is invoked, which never happens on net481 (TeamsOrchestrator import
        // dispatch is compiled out there). TryAdd keeps connector registrations authoritative.
        services.TryAddSingleton<ITeamTarget, CompositeTeamTarget>();

        services.AddScoped<TeamIterationsTeamExtension>(sp => new TeamIterationsTeamExtension(
            sp.GetRequiredService<IOptions<TeamsModuleOptions>>(),
            ResolveConnectorCapabilities(sp),
            sp.GetRequiredService<ITeamSource>(),
            sp.GetRequiredService<ITeamTarget>(),
            sp.GetService<INodeTranslationTool>(),
            sp.GetService<IReferencedPathLifecycle>(),
            sp.GetService<ILogger<TeamIterationsTeamExtension>>()));
        services.AddScoped<IModuleExtension>(sp => sp.GetRequiredService<TeamIterationsTeamExtension>());

        services.AddScoped<TeamMembersTeamExtension>(sp => new TeamMembersTeamExtension(
            sp.GetRequiredService<IOptions<TeamsModuleOptions>>(),
            ResolveConnectorCapabilities(sp),
            sp.GetRequiredService<ITeamSource>(),
            sp.GetRequiredService<ITeamTarget>(),
            sp.GetService<IIdentityTranslationTool>(),
            sp.GetService<IIdentitiesOrchestrator>(),
            sp.GetService<ILogger<TeamMembersTeamExtension>>()));
        services.AddScoped<IModuleExtension>(sp => sp.GetRequiredService<TeamMembersTeamExtension>());

        services.AddScoped<TeamCapacityTeamExtension>(sp => new TeamCapacityTeamExtension(
            sp.GetRequiredService<IOptions<TeamsModuleOptions>>(),
            ResolveConnectorCapabilities(sp),
            sp.GetRequiredService<ITeamSource>(),
            sp.GetRequiredService<ITeamTarget>(),
            sp.GetService<ILogger<TeamCapacityTeamExtension>>()));
        services.AddScoped<IModuleExtension>(sp => sp.GetRequiredService<TeamCapacityTeamExtension>());

        services.AddScoped<TeamAreaPathsTeamExtension>(sp => new TeamAreaPathsTeamExtension(
            ResolveConnectorCapabilities(sp),
            sp.GetRequiredService<ITeamSource>(),
            sp.GetRequiredService<ITeamTarget>(),
            sp.GetService<INodeTranslationTool>(),
            sp.GetService<ILogger<TeamAreaPathsTeamExtension>>()));
        services.AddScoped<IModuleExtension>(sp => sp.GetRequiredService<TeamAreaPathsTeamExtension>());

        return services;
    }

    /// <summary>
    /// Connector capability declaration (ADR-0024/EC-H1). Fail-closed: hosts that register
    /// no connector capability provider get an explicit None declaration — the same posture
    /// as the CommentsWorkItemExtension registration.
    /// </summary>
    private static IConnectorCapabilityProvider ResolveConnectorCapabilities(System.IServiceProvider sp)
        => sp.GetService<IConnectorCapabilityProvider>()
            ?? new Infrastructure.Agent.ConnectorCapability.StaticConnectorCapabilityProvider(
                global::DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability.None);
}
