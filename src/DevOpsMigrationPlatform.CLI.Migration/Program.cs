using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Create host builder using the centralized MigrationPlatformHost
            using var host = MigrationPlatformHost.CreateDefaultBuilder(args).Build();
            
            // Start the host and get the CommandApp
            await host.StartAsync();
            var commandApp = host.Services.GetRequiredService<CommandApp>();
            
            // Display application header
            AnsiConsole.Write(new FigletText("DevOps Migration").LeftJustified().Color(Color.Blue));
            AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
            
            // Execute the command with filtered args (--config removed by MigrationPlatformHost)
            var result = await commandApp.RunAsync(args);

            // Cleanup telemetry providers
            if (host.Services.GetService<TracerProvider>() is { } tp)
            {
                tp.ForceFlush(5000);
                tp.Dispose();
            }
            if (host.Services.GetService<MeterProvider>() is { } mp)
            {
                mp.ForceFlush(5000);
                mp.Dispose();
            }

            return result;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]❌ Unhandled exception during CLI execution[/]");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
            return 1;
        }
    }
}
