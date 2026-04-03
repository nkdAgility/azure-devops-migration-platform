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

    /// <param name="controlPlaneBaseUrl">
    /// Base URL of the control plane API, e.g.
    /// <c>http://localhost:5100</c> (Aspire local) or
    /// <c>https://control-plane.example.com</c> (cloud).
    /// </param>
    public ControlPlaneClient(string controlPlaneBaseUrl, ILogger<ControlPlaneClient> logger)
    {
        if (string.IsNullOrWhiteSpace(controlPlaneBaseUrl))
            throw new ArgumentException("controlPlaneBaseUrl must not be empty.", nameof(controlPlaneBaseUrl));

        _http = new HttpClient { BaseAddress = new Uri(controlPlaneBaseUrl) };
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

        // TODO: implement full control plane submission and progress polling
        // (docs/control-plane.md):
        //   1. POST /jobs  — submit MigrationJob; receive jobId confirmation
        //   2. Poll GET /jobs/{jobId}/progress at configurable interval
        //   3. Deserialise ProgressEvent items and yield them to the caller
        //   4. Stop polling when job reaches Completed / Failed / Cancelled state

        // Placeholder: single yield so the method is a legal IAsyncEnumerable
        await Task.Yield();
        yield return new ProgressEvent
        {
            Module = "ControlPlaneClient",
            Stage = "Submitted",
            Message = $"Job {job.JobId} submitted — polling not yet implemented"
        };

        _logger.LogWarning("ControlPlaneClient polling not yet implemented. Job {JobId} submitted only.", job.JobId);
    }

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
