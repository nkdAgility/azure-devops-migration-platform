using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Provides mock service provider implementations for CLI command testing.
/// Enables isolated testing with controlled service dependencies.
/// </summary>
public static class MockServiceProvider
{
    /// <summary>
    /// Creates a test service provider with basic CLI dependencies.
    /// </summary>
    /// <param name="configuration">Configuration to include in services</param>
    /// <returns>IServiceProvider for testing</returns>
    public static IServiceProvider Create(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        
        // Add configuration
        services.AddSingleton(configuration);
        
        // Add mock logging
        services.AddLogging(builder => builder.AddConsole());
        
        // Add mock host lifetime
        var mockLifetime = new Mock<IHostApplicationLifetime>();
        services.AddSingleton(mockLifetime.Object);
        
        // Add real activity source for telemetry (ActivitySource cannot be mocked)
        var activitySource = new ActivitySource("test");
        services.AddSingleton(activitySource);
        
        // Add options pattern support
        services.AddOptions();
        
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a test service provider with additional mock services for specific commands.
    /// </summary>
    /// <param name="configuration">Configuration to include in services</param>
    /// <param name="additionalServices">Additional services to register</param>
    /// <returns>IServiceProvider for testing</returns>
    public static IServiceProvider CreateWithServices(
        IConfiguration configuration, 
        Action<IServiceCollection> additionalServices)
    {
        var services = new ServiceCollection();
        
        // Add basic services
        services.AddSingleton(configuration);
        services.AddLogging(builder => builder.AddConsole());
        
        // Add host lifetime with controllable behavior
        var mockLifetime = new Mock<IHostApplicationLifetime>();
        services.AddSingleton(mockLifetime.Object);
        
        // Add activity source for telemetry
        services.AddSingleton(provider => new ActivitySource("DevOpsMigrationPlatform.CLI.Tests"));
        
        // Add options support
        services.AddOptions();
        
        // Add custom services
        additionalServices(services);
        
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a mock IHostApplicationLifetime with verifiable behavior.
    /// </summary>
    /// <returns>Mock that can verify StopApplication() was called</returns>
    public static Mock<IHostApplicationLifetime> CreateMockLifetime()
    {
        var mockLifetime = new Mock<IHostApplicationLifetime>(MockBehavior.Strict);
        
        // Setup cancellation tokens
        mockLifetime.Setup(x => x.ApplicationStarted).Returns(new CancellationToken());
        mockLifetime.Setup(x => x.ApplicationStopping).Returns(new CancellationToken());
        mockLifetime.Setup(x => x.ApplicationStopped).Returns(new CancellationToken());
        
        // Setup StopApplication to be verifiable
        mockLifetime.Setup(x => x.StopApplication());
        
        return mockLifetime;
    }

    /// <summary>
    /// Creates a service provider specifically for testing configuration binding.
    /// </summary>
    /// <typeparam name="TOptions">Options type to bind</typeparam>
    /// <param name="configuration">Configuration to bind from</param>
    /// <param name="sectionName">Configuration section name</param>
    /// <returns>IServiceProvider with bound options</returns>
    public static IServiceProvider CreateForOptions<TOptions>(
        IConfiguration configuration, 
        string sectionName = "") where TOptions : class
    {
        var services = new ServiceCollection();
        
        services.AddSingleton(configuration);
        
        // Configure options binding
        if (string.IsNullOrEmpty(sectionName))
        {
            services.Configure<TOptions>(configuration);
        }
        else
        {
            services.Configure<TOptions>(configuration.GetSection(sectionName));
        }
        
        // Add other required services
        services.AddLogging(builder => builder.AddConsole());
        var mockLifetime = new Mock<IHostApplicationLifetime>();
        services.AddSingleton(mockLifetime.Object);
        
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a service provider that will throw exceptions for testing error handling.
    /// </summary>
    /// <param name="serviceTypeToFail">Type of service that should throw when resolved</param>
    /// <param name="exception">Exception to throw</param>
    /// <returns>IServiceProvider that throws on specific service resolution</returns>
    public static IServiceProvider CreateFailingProvider(Type serviceTypeToFail, Exception exception)
    {
        var services = new ServiceCollection();
        
        // Add basic working services
        services.AddSingleton(InMemoryTestConfiguration.CreateDefault());
        services.AddLogging(builder => builder.AddConsole());
        
        // Add failing service
        services.AddTransient(serviceTypeToFail, _ => throw exception);
        
        return services.BuildServiceProvider();
    }
}