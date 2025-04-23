using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Infrastructure.Options;
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
                services.AddSingleton<ICatalogService, CatalogService>();

                services.Configure<MigrationPlatformOptions>(context.Configuration.GetSection("MigrationPlatformCli"));
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
