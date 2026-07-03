// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Migration.Views;
using DevOpsMigrationPlatform.Infrastructure.Config;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.CLI.Migration.Configuration;

/// <summary>
/// Module entry point for the interactive configuration wizard.
/// Registers the services required by the <c>config new</c> / <c>configure</c> commands.
/// </summary>
internal static class ConfigurationWizardServiceCollectionExtensions
{
    /// <summary>
    /// Registers the configuration service, interactive wizard builder, and renderer
    /// used by the configuration wizard commands.
    /// </summary>
    public static IServiceCollection AddConfigurationWizard(this IServiceCollection services)
    {
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IInteractiveConfigurationBuilder, InteractiveConfigurationBuilder>();
        services.AddSingleton<ConfigureCommandRenderer>();
        return services;
    }
}
