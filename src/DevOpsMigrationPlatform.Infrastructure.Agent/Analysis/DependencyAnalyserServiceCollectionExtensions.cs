// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

public static class DependencyAnalyserServiceCollectionExtensions
{
    public static IServiceCollection AddDependencyAnalyserServices(this IServiceCollection services)
    {
        services.AddSingleton<IDependencyOrchestrator, DependencyOrchestrator>();
        services.AddTransient<IOrganisationsAnalyser, DependencyAnalyser>();
        services.AddTransient<IAnalyser>(sp => sp.GetRequiredService<IOrganisationsAnalyser>());
        // Per-project capture module — dispatched by the plan executor for capture.dependencies.* tasks.
        services.AddTransient<IModule, DependenciesModule>();
        return services;
    }
}

