using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI.JobRunners;

/// <summary>
/// Submits a <see cref="MigrationJob"/> to a running Control Plane over HTTP,
/// then polls the progress endpoint and streams <see cref="ProgressEvent"/> items
/// back to the caller until the job reaches a terminal state.
///
/// Used for both:
///   - Local Aspire mode   — endpoint is http://localhost:5100 (Aspire-started control plane)
///   - Cloud mode          — endpoint is the Azure Container Apps HTTPS URL
///
/// Switching between the two requires only a config change in appsettings.json;
/// no code changes are needed.  See docs/cli.md and docs/control-plane.md.
/// </summary>
public sealed class ControlPlaneClient : IJobRunner, ILogsClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly ILogger<ControlPlaneClient> _logger;

    /// <param name="http">
    /// Named/typed <see cref="HttpClient"/> pre-configured with the control plane base address.
    /// Provided by <see cref="System.Net.Http.IHttpClientFactory"/> via DI.
    /// </param>
    public ControlPlaneClient(HttpClient http, ILogger<ControlPlaneClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ProgressEvent> RunAsync(
        MigrationJob job,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        _logger.LogInformation(
            "ControlPlaneClient submitting job {JobId} to {BaseAddress}",
            job.JobId, _http.BaseAddress);

        // 1. POST /jobs — submit MigrationJob; receive jobId confirmation.
        using var submitResponse = await _http
            .PostAsJsonAsync("/jobs", job, _jsonOptions, ct)
            .ConfigureAwait(false);

        submitResponse.EnsureSuccessStatusCode();

        var submitResult = await submitResponse.Content
            .ReadFromJsonAsync<SubmitJobResponse>(_jsonOptions, ct)
            .ConfigureAwait(false);

        var jobId = submitResult?.JobId
            ?? throw new InvalidOperationException("Control plane did not return a jobId.");

        _logger.LogInformation("Job {JobId} accepted by control plane.", jobId);

        // 2. Stream progress via SSE until the job reaches a terminal state.
        await foreach (var evt in FollowLogsAsync(jobId, ct).ConfigureAwait(false))
            yield return evt;
    }

    private sealed record SubmitJobResponse(Guid JobId);

    /// <summary>
    /// Returns a snapshot of stored ProgressEvents for <paramref name="jobId"/>.
    /// Calls <c>GET /jobs/{jobId}/logs</c> and deserialises the JSON array.
    /// </summary>
    public async Task<IReadOnlyList<ProgressEvent>> GetLogsAsync(Guid jobId, CancellationToken ct)
    {
        var response = await _http
            .GetAsync($"/jobs/{jobId}/logs", ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var events = await response.Content
            .ReadFromJsonAsync<List<ProgressEvent>>(_jsonOptions, ct)
            .ConfigureAwait(false);

        return events ?? [];
    }

    /// <summary>
    /// Streams live ProgressEvents from <c>GET /jobs/{jobId}/logs?follow=true</c> (SSE).
    /// Yields each event as it arrives; breaks on <c>event: job-ended</c> or cancellation.
    /// </summary>
    public async IAsyncEnumerable<ProgressEvent> FollowLogsAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await _http
            .GetAsync($"/jobs/{jobId}/logs?follow=true",
                HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            if (line.StartsWith("event:") && line.Contains("job-ended"))
                yield break;

            if (!line.StartsWith("data:"))
                continue;

            var json = line["data:".Length..].Trim();
            if (string.IsNullOrEmpty(json))
                continue;

            var evt = JsonSerializer.Deserialize<ProgressEvent>(json, _jsonOptions);
            if (evt is not null)
                yield return evt;
        }
    }
}
