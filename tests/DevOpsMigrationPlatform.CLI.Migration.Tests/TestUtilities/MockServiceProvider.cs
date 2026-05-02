// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Provides mock service provider implementations for CLI command testing.
/// </summary>
public static class MockServiceProvider
{
    /// <summary>
    /// Creates a test service provider with basic CLI dependencies.
    /// </summary>
    public static IServiceProvider Create(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(builder => builder.AddConsole());
        services.AddOptions();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a test service provider with additional mock services.
    /// </summary>
    public static IServiceProvider CreateWithServices(
        IConfiguration configuration,
        Action<IServiceCollection> additionalServices)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(builder => builder.AddConsole());
        services.AddOptions();
        additionalServices(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a service provider for testing options binding.
    /// </summary>
    public static IServiceProvider CreateForOptions<TOptions>(
        IConfiguration configuration,
        string sectionName = "") where TOptions : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);

        if (string.IsNullOrEmpty(sectionName))
            services.Configure<TOptions>(configuration);
        else
            services.Configure<TOptions>(configuration.GetSection(sectionName));

        services.AddLogging(builder => builder.AddConsole());
        return services.BuildServiceProvider();
    }
}