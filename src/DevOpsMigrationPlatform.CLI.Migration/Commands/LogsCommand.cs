using System.Diagnostics;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.JobRunners;
using OpenTelemetry.Trace;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace DevOpsMigrationPlatform.CLI.Commands;

public sealed class LogsCommand : AsyncCommand<LogsCommand.Settings>
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

    private readonly ILogsClient _client;
    private readonly ActivitySource _activitySource;

    public LogsCommand(ILogsClient client, ActivitySource activitySource)
    {
        _client         = client;
        _activitySource = activitySource;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var activity = _activitySource.StartActivity("logs");
        try
        {
            return await RunCoreAsync(settings, activity);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return 1;
        }
    }

    private async Task<int> RunCoreAsync(Settings settings, Activity? activity)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (!settings.Follow)
        {
            var events = await _client.GetLogsAsync(settings.JobId, cts.Token);
            foreach (var evt in events)
                Console.WriteLine(JsonSerializer.Serialize(evt, _jsonOptions));
            return 0;
        }

        await foreach (var evt in _client.FollowLogsAsync(settings.JobId, cts.Token))
            Console.WriteLine(JsonSerializer.Serialize(evt, _jsonOptions));

        return 0;
    }
}
