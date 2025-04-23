using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.CLI.Commands;
using MigrationPlatform.CLI.ConfigCommands;
using MigrationPlatform.CLI.Options;
using MigrationPlatform.CLI.Services;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Extensions.Hosting;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MigrationPlatform.CLI
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {

            var builder = Host.CreateDefaultBuilder(args);

            builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IConfiguration>(context.Configuration);
                services.AddSingleton<ILogger>(Log.Logger);
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
                  builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
                  builder.AddEnvironmentVariables();
                  builder.AddCommandLine(args);
              });

            builder.UseSpectreConsole(config =>
            {
                config.SetApplicationName("devopsmigration");
                config.PropagateExceptions();
                config.ValidateExamples();

                config.SetApplicationVersion(GetRunningVersion().versionString);

                config.AddBranch("config", branch =>
                {
                    branch.SetDescription("Tools manipulating and setting up configurations");
                    branch.AddCommand<ConfigSetConfigStorageCommand>("setfolder")
                        .WithDescription("Sets the folder to use to store your configurations")
                        .WithExample(new[] { "config", "setfolder", "--path", "%userprofile%\\AzureDevOpsMigrationTools" });
                    branch.AddCommand<ConfigSetConfigStorageCommand>("create")
                        .WithDescription("Add or update an Azure DevOps configuration. For example, which server or account plus auth information.")
                        .WithExample(new[] { "config", "create" });

                });

                config.AddCommand<DiscoveryCommand>("discovery")
                    .WithDescription("Discover the contents of your Azure DevOps instance")
                    .WithExample(new[] { "discovery", "--organisation", "", "--token", "" });

                config.AddCommand<TfsExportCommand>("tfsexport")
                   .WithDescription("Exports the data from TFS")
                   .WithExample(new[] { "tfsexport", "--tfsserver", "https://localhost/tfs", "--project", "My Project" });
            });

            try
            {
                AnsiConsole.Write(new FigletText("Azure DevOps Migration Platform").LeftJustified().Color(Color.Red));
                AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
                await builder.RunConsoleAsync();
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]❌ Unhandled exception during CLI execution[/]");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
                return 1;
            }

        }

        public static (Version version, string PreReleaseLabel, string versionString) GetRunningVersion()
        {
            FileVersionInfo myFileVersionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetEntryAssembly()?.Location);
            var matches = Regex.Matches(myFileVersionInfo.ProductVersion, @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<build>0|[1-9]\d*)(?:-((?<label>:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<fullEnd>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$");
            Version version = new Version(myFileVersionInfo.FileVersion);
            string textVersion = "v" + version.Major + "." + version.Minor + "." + version.Build + "-" + matches[0].Groups[1].Value;
            return (version, matches[0].Groups[1].Value, textVersion);
        }

    }
}
