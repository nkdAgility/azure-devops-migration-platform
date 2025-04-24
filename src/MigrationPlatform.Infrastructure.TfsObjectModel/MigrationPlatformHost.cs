using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using Microsoft.VisualStudio.Services.Client;
using MigrationPlatform.Abstractions.Options;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Infrastructure.Repositories;
using MigrationPlatform.Infrastructure.Services;

namespace MigrationPlatform.Infrastructure.TfsObjectModel
{
    public static class MigrationPlatformHost
    {

        public class Settings
        {

            public Settings(Uri tfsServer, string project, string outputFolder)
            {
                TfsServer = tfsServer;
                Project = project;
                OutputFolder = outputFolder;
            }

            public Uri TfsServer { get; set; }

            public string Project { get; set; }

            public string OutputFolder { get; set; }
        }

        public static IHostBuilder CreateDefaultBuilder(string[] args, Settings settings)
        {
            var builder = Host.CreateDefaultBuilder();

            builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IConfiguration>(context.Configuration);
                services.AddSingleton<IWorkItemExportService, WorkItemExportService>();
                services.AddSingleton<IMigrationRepository, MigrationRepository>();

                services.Configure<MigrationRepositoryOptions>(context.Configuration.GetSection("MigrationRepository"));

                services.AddSingleton<TfsTeamProjectCollection>(_ =>
                {
                    var creds = new VssClientCredentials(true);
                    var collection = new TfsTeamProjectCollection(settings.TfsServer, creds);
                    collection.EnsureAuthenticated();
                    return collection;
                });

                services.AddSingleton<WorkItemStore>(provider =>
                {
                    var collection = provider.GetRequiredService<TfsTeamProjectCollection>();
                    return collection.GetService<WorkItemStore>();
                });

                services.AddSingleton<WorkItemServer>(provider =>
                {
                    var collection = provider.GetRequiredService<TfsTeamProjectCollection>();
                    return collection.GetService<WorkItemServer>();
                });

                services.PostConfigure<MigrationRepositoryOptions>(options =>
                {
                    options.RepositoryPath = settings.OutputFolder;
                });
            });

            builder.UseConsoleLifetime(configureOptions =>
            {
                configureOptions.SuppressStatusMessages = true;
            });

            builder.ConfigureAppConfiguration(builder =>
            {
                builder.SetBasePath(settings.OutputFolder);
                builder.AddJsonFile(Path.Combine(settings.OutputFolder, "configuration.json"), optional: true);
                builder.AddEnvironmentVariables();
                builder.AddCommandLine(args);
            });
            return builder;

        }
    }
}
