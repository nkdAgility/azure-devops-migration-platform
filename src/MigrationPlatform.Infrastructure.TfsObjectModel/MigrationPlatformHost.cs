using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Infrastructure.Repositories;
using MigrationPlatform.Infrastructure.Services;

namespace MigrationPlatform.Infrastructure.TfsObjectModel
{
    public static class MigrationPlatformHost
    {
        public static IHostBuilder CreateDefaultBuilder()
        {
            var builder = Host.CreateDefaultBuilder();

            builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IConfiguration>(context.Configuration);
                services.AddSingleton<IWorkItemExportService, WorkItemExportService>();
                services.AddSingleton<IMigrationRepository, MigrationRepository>();
            });

            builder.UseConsoleLifetime(configureOptions =>
            {
                configureOptions.SuppressStatusMessages = true;
            });

            builder.ConfigureAppConfiguration(builder =>
            {
                //builder.SetBasePath(AppContext.BaseDirectory);
                //builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettssings.json"), optional: false);
                builder.AddEnvironmentVariables();
                //builder.AddCommandLine(args);
            });
            return builder;


        }
    }
}
