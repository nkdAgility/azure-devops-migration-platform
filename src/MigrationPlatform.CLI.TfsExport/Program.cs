using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Abstractions.Utilities;
using MigrationPlatform.CLI.Commands;
using MigrationPlatform.Infrastructure.Repositories;
using MigrationPlatform.Infrastructure.Services;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Extensions.Hosting;
using System.Diagnostics;

namespace MigrationPlatform.TfsExport
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
#if DEBUG
            if (!Debugger.IsAttached)
            {
                Debugger.Launch(); // Prompts to attach your debugger
            }
#endif
            var builder = Host.CreateDefaultBuilder(args);

            builder.ConfigureServices((context, services) =>
            {
                services.AddSingleton<IConfiguration>(context.Configuration);
                services.AddSingleton<ILogger>(Log.Logger);
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
                builder.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
                builder.AddEnvironmentVariables();
                builder.AddCommandLine(args);
            });


            builder.UseSpectreConsole<ExportCommand>(config =>
            {
                config.SetApplicationName("tfsexport");
                config.PropagateExceptions();
                config.ValidateExamples();

                config.SetApplicationVersion(VersionUtilities.GetRunningVersion().versionString);

                config.AddCommand<ExportCommand>("export")
                    .WithDescription("Exports the data from TFS")
                .WithExample(new[] { "export", "--tfsserver", "https://localhost/tfs", "--project", "My Project" });

            });

            try
            {
                AnsiConsole.Write(new FigletText("TFS Exporter").LeftJustified().Color(Color.Red));
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


    }
}
