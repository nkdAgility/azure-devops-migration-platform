// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

public static class DependencyAnalyserServiceCollectionExtensions
{
    public static IServiceCollection AddDependencyAnalyserServices(this IServiceCollection services)
    {
        services.AddSingleton<IDependencyOrchestrator, DependencyOrchestrator>();
        services.AddTransient<IAnalyser, DependencyAnalyser>();
        return services;
    }
}

