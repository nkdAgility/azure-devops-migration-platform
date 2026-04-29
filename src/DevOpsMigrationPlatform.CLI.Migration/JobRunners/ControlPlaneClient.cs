using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
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
public sealed class ControlPlaneClient : IJobSubmissionClient, ILogsClient, IControlPlaneClient
{
    private readonly JsonSerializerOptions _jsonOptions;

    private readonly HttpClient _http;
    private readonly ILogger<ControlPlaneClient> _logger;

    /// <param name="http">
    /// Named/typed <see cref="HttpClient"/> pre-configured with the control plane base address.
    /// Provided by <see cref="System.Net.Http.IHttpClientFactory"/> via DI.
    /// </param>
    public ControlPlaneClient(
        HttpClient http,
        ILogger<ControlPlaneClient> logger,
        PolymorphicEndpointOptionsConverter? endpointConverter = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        if (endpointConverter is not null)
            _jsonOptions.Converters.Add(endpointConverter);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ProgressEvent> RunAsync(
        MigrationJob job,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var jobId = await SubmitAsync(job, ct).ConfigureAwait(false);

        // 2. Stream progress via SSE until the job reaches a terminal state.
        await foreach (var evt in FollowLogsAsync(jobId, ct).ConfigureAwait(false))
            yield return evt;
    }

    /// <summary>
    /// Submits a <see cref="Job"/> to the control plane and returns the assigned jobId.
    /// Does not follow progress — use <see cref="FollowLogsAsync"/> or <see cref="StreamDiagnosticsAsync"/>
    /// separately for live streaming.
    /// </summary>
    public async Task<Guid> SubmitAsync(Job job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        _logger.LogInformation(
            "ControlPlaneClient submitting job {JobId} to {BaseAddress}",
            job.JobId, _http.BaseAddress);

        using var submitResponse = await _http
            .PostAsJsonAsync("/jobs", job, _jsonOptions, ct)
            .ConfigureAwait(false);

        if (!submitResponse.IsSuccessStatusCode)
        {
            var body = await submitResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError(
                "Control plane rejected job {JobId}: {StatusCode} — {Body}",
                job.JobId, (int)submitResponse.StatusCode, body);
            submitResponse.EnsureSuccessStatusCode(); // rethrow with status code
        }

        var submitResult = await submitResponse.Content
            .ReadFromJsonAsync<SubmitJobResponse>(_jsonOptions, ct)
            .ConfigureAwait(false);

        var jobId = submitResult?.JobId
            ?? throw new InvalidOperationException("Control plane did not return a jobId.");

        _logger.LogInformation("Job {JobId} accepted by control plane.", jobId);
        return jobId;
    }

    private sealed record SubmitJobResponse(Guid JobId);

    /// <summary>    /// Returns all jobs visible to the caller via <c>GET /jobs</c>.
    /// </summary>
    public async Task<IReadOnlyList<JobSummary>> GetAllJobsAsync(CancellationToken ct)
    {
        var response = await _http.GetAsync("/jobs", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var summaries = await response.Content
            .ReadFromJsonAsync<List<JobSummary>>(_jsonOptions, ct)
            .ConfigureAwait(false);
        return summaries ?? [];
    }

    /// <summary>
    /// Returns the latest <see cref="JobMetrics"/> for a job, or <c>null</c> when none pushed yet.
    /// Calls <c>GET /jobs/{jobId}/telemetry</c>.
    /// </summary>
    public async Task<JobMetrics?> GetTelemetryAsync(Guid jobId, CancellationToken ct)
    {
        var response = await _http
            .GetAsync($"/jobs/{jobId}/telemetry", ct)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<JobMetrics>(_jsonOptions, ct)
            .ConfigureAwait(false);
    }

    /// <summary>    /// Returns a snapshot of stored ProgressEvents for <paramref name="jobId"/>.
    /// Calls <c>GET /jobs/{jobId}/progress</c> and deserialises the JSON array.
    /// </summary>
    public async Task<IReadOnlyList<ProgressEvent>> GetProgressAsync(Guid jobId, CancellationToken ct)
    {
        var response = await _http
            .GetAsync($"/jobs/{jobId}/progress", ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var events = await response.Content
            .ReadFromJsonAsync<List<ProgressEvent>>(_jsonOptions, ct)
            .ConfigureAwait(false);

        return events ?? [];
    }

    /// <summary>
    /// Streams live ProgressEvents from <c>GET /jobs/{jobId}/progress?follow=true</c> (SSE).
    /// Yields each event as it arrives; breaks on <c>event: job-ended</c> or cancellation.
    /// </summary>
    public async IAsyncEnumerable<ProgressEvent> FollowLogsAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken ct,
        long? lastEventSequence = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/jobs/{jobId}/progress?follow=true");
        if (lastEventSequence.HasValue)
            request.Headers.TryAddWithoutValidation("Last-Event-ID",
                lastEventSequence.Value.ToString());

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false, bufferSize: 256);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            if (line.StartsWith("event:") && line.Contains("job-failed"))
                throw new InvalidOperationException("Job failed on the agent. Check agent logs for details.");

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

    /// <summary>
    /// Streams live <see cref="DiagnosticLogRecord"/> from
    /// <c>GET /jobs/{jobId}/diagnostics?follow=true&amp;level={level}</c> (SSE).
    /// Yields each record as it arrives; breaks on <c>event: job-ended</c> or cancellation.
    /// </summary>
    public async IAsyncEnumerable<DiagnosticLogRecord> StreamDiagnosticsAsync(
        Guid jobId,
        string? level,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = $"/jobs/{jobId}/diagnostics?follow=true";
        if (!string.IsNullOrEmpty(level))
            url += $"&level={Uri.EscapeDataString(level)}";

        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false, bufferSize: 256);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            if (line.StartsWith("event:") &&
                (line.Contains("job-ended") || line.Contains("job-failed")))
                yield break;

            if (!line.StartsWith("data:"))
                continue;

            var json = line["data:".Length..].Trim();
            if (string.IsNullOrEmpty(json))
                continue;

            var record = JsonSerializer.Deserialize<DiagnosticLogRecord>(json, _jsonOptions);
            if (record is not null)
                yield return record;
        }
    }

    // ── Discovery Job API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Submits a <see cref="DiscoveryJob"/> to the control plane via the same
    /// <c>POST /jobs</c> endpoint used by migration jobs.
    /// Returns the assigned jobId.
    /// </summary>
    public Task<Guid> SubmitDiscoveryAsync(DiscoveryJob job, CancellationToken ct = default)
        => SubmitAsync(job, ct);

    /// <summary>
    /// Streams live <see cref="ProgressEvent"/> records for a discovery job via SSE.
    /// Uses the same progress endpoint as migration jobs.
    /// </summary>
    public async IAsyncEnumerable<ProgressEvent> FollowDiscoveryLogsAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in FollowLogsAsync(jobId, ct).ConfigureAwait(false))
            yield return evt;
    }

    /// <summary>
    /// Returns the job bootstrap data required by the Migration Agent to start work.
    /// </summary>
    public async Task<JobBootstrap?> GetBootstrapAsync(Guid jobId, CancellationToken ct)
    {
        var response = await _http
            .GetAsync($"/jobs/{jobId}/bootstrap", ct)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<JobBootstrap>(_jsonOptions, ct)
            .ConfigureAwait(false);
    }
}
