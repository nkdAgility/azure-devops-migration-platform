// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

public static class InventoryAnalyserServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryAnalyserServices(this IServiceCollection services)
    {
        services.AddTransient<IAnalyser, InventoryAnalyser>();
        return services;
    }
}

