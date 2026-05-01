using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.SchemaGenerator;

/// <summary>
/// Schema generator entry point. Builds a DI container with all connector
/// and module registrations, then generates migration.schema.json.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2 || args[0] != "--output")
        {
            Console.Error.WriteLine("Usage: SchemaGenerator --output <path>");
            return 1;
        }

        var outputPath = args[1];

        var builder = Host.CreateApplicationBuilder();

        // Register all platform services (same as agent host)
        var services = builder.Services;

        // Core configuration
        services.AddSingleton(builder.Configuration);

        // Register all connector services
        services.AddSimulatedServices();
        services.AddAzureDevOpsWorkItemExport();
        services.AddAzureDevOpsWorkItemImport();

        // Register module services
        services.AddIdentitiesModule(builder.Configuration);
        services.AddNodesModule(builder.Configuration);
        services.AddTeamsModule(builder.Configuration);
        services.AddWorkItemsModule();

        // Register tool services
        services.AddFieldTransformToolServices();
        services.AddNodeTranslationToolServices();
        // Note: IdentityLookupToolServices is registered by AddIdentitiesModule

        // Register core infrastructure services (for EndpointOptionsTypeRegistry)
        services.AddMigrationPlatformPolymorphicSerializers();

        // Register logging
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        // Build service provider
        var serviceProvider = services.BuildServiceProvider();

        // Run schema generator
        var logger = serviceProvider.GetRequiredService<ILogger<SchemaGeneratorHost>>();
        var host = new SchemaGeneratorHost(serviceProvider, logger);

        return await host.RunAsync(outputPath);
    }
}
