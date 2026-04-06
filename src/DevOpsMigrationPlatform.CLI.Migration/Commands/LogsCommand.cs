using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace DevOpsMigrationPlatform.CLI.Commands;

public sealed class LogsCommand : CommandBase<LogsCommand.Settings>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to retrieve logs for")]
        public Guid JobId { get; init; }

        [CommandOption("--follow")]
        [Description("Follow live events via SSE until the job completes")]
        public bool Follow { get; init; }
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        // Create command-specific host with control plane client
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            var controlPlaneBaseUrl = config["ControlPlane:BaseUrl"] ?? "http://localhost:5100";
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ControlPlaneClient>>();
                return new ControlPlaneClient(controlPlaneBaseUrl, logger);
            });
            services.AddSingleton<ILogsClient>(sp => sp.GetRequiredService<ControlPlaneClient>());
        });

        try
        {
            return await RunCoreAsync(settings);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> RunCoreAsync(Settings settings)
    {
        var client = GetRequiredService<ILogsClient>();
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (!settings.Follow)
        {
            var events = await client.GetLogsAsync(settings.JobId, cts.Token);
            foreach (var evt in events)
                Console.WriteLine(JsonSerializer.Serialize(evt, _jsonOptions));
            return 0;
        }

        await foreach (var evt in client.FollowLogsAsync(settings.JobId, cts.Token))
            Console.WriteLine(JsonSerializer.Serialize(evt, _jsonOptions));

        return 0;
    }
}
