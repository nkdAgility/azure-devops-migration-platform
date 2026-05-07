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
        services.AddTransient<IAnalyser, DependencyAnalyser>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="DependencyCapture"/> as an <see cref="ICapture"/> singleton.
    /// Call this in any agent host that supports dependency capture (ADO and Simulated).
    /// TFS agents must NOT call this method.
    /// </summary>
    public static IServiceCollection AddDependencyCapture(this IServiceCollection services)
    {
        services.AddSingleton<ICapture, DependencyCapture>();
        return services;
    }
}

