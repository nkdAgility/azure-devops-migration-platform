// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI.JobRunners;

/// <summary>
/// Submits a <see cref="Job"/> to a running Control Plane over HTTP,
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
    private readonly ControlPlaneCommunicationRecorder? _diagnosticsRecorder;

    private readonly HttpClient _http;
    private readonly ILogger<ControlPlaneClient> _logger;

    /// <param name="http">
    /// Named/typed <see cref="HttpClient"/> pre-configured with the control plane base address.
    /// Provided by <see cref="System.Net.Http.IHttpClientFactory"/> via DI.
    /// </param>
    public ControlPlaneClient(
        HttpClient http,
        ILogger<ControlPlaneClient> logger,
        PolymorphicEndpointOptionsConverter? endpointConverter = null,
        ControlPlaneCommunicationRecorder? diagnosticsRecorder = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _diagnosticsRecorder = diagnosticsRecorder;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        if (endpointConverter is not null)
            _jsonOptions.Converters.Add(endpointConverter);
    }

    /// <summary>
    /// Submits a <see cref="Job"/> to the control plane and returns the assigned jobId.
    /// Use <see cref="StreamJobAsync"/> for live streaming.
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
        _logger.LogInformation("ControlPlaneClient calling GET /jobs");
        var response = await _http.GetAsync("/jobs", ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var summaries = await response.Content
            .ReadFromJsonAsync<List<JobSummary>>(_jsonOptions, ct)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Control plane response GET /jobs => {StatusCode}, jobs={Count}",
            (int)response.StatusCode,
            summaries?.Count ?? 0);
        return summaries ?? [];
    }

    /// <summary>    /// Returns a snapshot of stored ProgressEvents for <paramref name="jobId"/>.
    /// Calls <c>GET /jobs/{jobId}/progress</c> and deserialises the JSON array.
    /// </summary>
    public async Task<IReadOnlyList<ProgressEvent>> GetProgressAsync(Guid jobId, CancellationToken ct)
    {
        _logger.LogInformation("ControlPlaneClient calling GET /jobs/{JobId}/progress", jobId);
        var response = await _http
            .GetAsync($"/jobs/{jobId}/progress", ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var events = await response.Content
            .ReadFromJsonAsync<List<ProgressEvent>>(_jsonOptions, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Control plane response GET /jobs/{JobId}/progress => {StatusCode}, events={Count}",
            jobId,
            (int)response.StatusCode,
            events?.Count ?? 0);

        return events ?? [];
    }

    // ── Unified stream ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the unified SSE stream at <c>GET /jobs/{jobId}/stream?from={fromSeq}</c>
    /// and yields <see cref="JobStreamEvent"/> records until the stream closes.
    /// Handles <c>event: progress</c>, <c>event: diagnostic</c>, and <c>event: job-ended</c>
    /// / <c>event: job-failed</c> (terminal).
    /// </summary>
    public async IAsyncEnumerable<JobStreamEvent> StreamJobAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken ct,
        long fromSeq = 0)
    {
        _logger.LogInformation(
            "ControlPlaneClient opening unified SSE stream GET /jobs/{JobId}/stream?from={FromSeq}",
            jobId, fromSeq);

        using var response = await _http
            .GetAsync($"/jobs/{jobId}/stream?from={fromSeq}", HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false, bufferSize: 256);

        string? eventType = null;
        long seq = 0;

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            if (line.StartsWith("id:"))
            {
                long.TryParse(line["id:".Length..].Trim(), out seq);
                continue;
            }

            if (line.StartsWith("event:"))
            {
                eventType = line["event:".Length..].Trim();
                if (eventType == "job-ended")
                {
                    yield return new JobStreamEvent(seq, JobStreamEventKind.Terminal,
                        null, null, false, null);
                    yield break;
                }
                if (eventType == "job-failed")
                {
                    yield return new JobStreamEvent(seq, JobStreamEventKind.Terminal,
                        null, null, true, "Job failed on the agent.");
                    yield break;
                }
                continue;
            }

            if (!line.StartsWith("data:"))
            {
                eventType = null;
                continue;
            }

            var json = line["data:".Length..].Trim();
            if (string.IsNullOrEmpty(json) || json == "{}")
                continue;

            if (eventType == "progress")
            {
                var evt = JsonSerializer.Deserialize<ProgressEvent>(json, _jsonOptions);
                if (evt is not null)
                {
                    await RecordProgressAsync(evt, json, ct).ConfigureAwait(false);
                    yield return new JobStreamEvent(seq, JobStreamEventKind.Progress, evt, null, null, null);
                }
            }
            else if (eventType == "diagnostic")
            {
                var record = JsonSerializer.Deserialize<DiagnosticLogRecord>(json, _jsonOptions);
                if (record is not null)
                {
                    await RecordDiagnosticAsync(record, json, ct).ConfigureAwait(false);
                    yield return new JobStreamEvent(seq, JobStreamEventKind.Diagnostic, null, record, null, null);
                }
            }

            eventType = null;
        }
    }

    /// <summary>
    /// Returns the job bootstrap data required by the Migration Agent to start work.
    /// </summary>
    public async Task<JobBootstrap?> GetBootstrapAsync(Guid jobId, CancellationToken ct)
    {
        _logger.LogInformation("ControlPlaneClient calling GET /jobs/{JobId}/bootstrap", jobId);
        var response = await _http
            .GetAsync($"/jobs/{jobId}/bootstrap", ct)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            _logger.LogInformation(
                "Control plane response GET /jobs/{JobId}/bootstrap => {StatusCode} (no bootstrap yet)",
                jobId,
                (int)response.StatusCode);
            return null;
        }

        response.EnsureSuccessStatusCode();
        var bootstrapJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        await RecordJsonAsync("bootstrap", bootstrapJson, ct).ConfigureAwait(false);
        var bootstrap = JsonSerializer.Deserialize<JobBootstrap>(bootstrapJson, _jsonOptions);
        _logger.LogInformation(
            "Control plane response GET /jobs/{JobId}/bootstrap => {StatusCode} {Bootstrap}",
            jobId,
            (int)response.StatusCode,
            System.Text.Json.JsonSerializer.Serialize(bootstrap, _jsonOptions));
        return bootstrap;
    }

    /// <summary>
    /// Returns the current <see cref="JobTaskList"/> for a job, or <c>null</c> when the
    /// agent has not yet pushed an execution plan.
    /// Calls <c>GET /jobs/{jobId}/tasks</c>.
    /// </summary>
    public async Task<JobTaskList?> GetTasksAsync(Guid jobId, CancellationToken ct)
    {
        _logger.LogInformation("ControlPlaneClient calling GET /jobs/{JobId}/tasks", jobId);
        var response = await _http
            .GetAsync($"/jobs/{jobId}/tasks", ct)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            _logger.LogInformation(
                "Control plane response GET /jobs/{JobId}/tasks => {StatusCode} (tasks unavailable yet)",
                jobId,
                (int)response.StatusCode);
            return null;
        }

        response.EnsureSuccessStatusCode();
        var taskList = await response.Content
            .ReadFromJsonAsync<JobTaskList>(_jsonOptions, ct)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "Control plane response GET /jobs/{JobId}/tasks => {StatusCode}, taskCount={TaskCount}",
            jobId,
            (int)response.StatusCode,
            taskList?.Tasks?.Count ?? 0);
        return taskList;
    }

    private Task RecordJsonAsync(string kind, string json, CancellationToken ct)
        => _diagnosticsRecorder is null
            ? Task.CompletedTask
            : _diagnosticsRecorder.RecordJsonAsync(kind, json, ct);

    private Task RecordProgressAsync(ProgressEvent progressEvent, string json, CancellationToken ct)
        => _diagnosticsRecorder is null
            ? Task.CompletedTask
            : _diagnosticsRecorder.RecordProgressAsync(progressEvent, json, ct);

    private Task RecordDiagnosticAsync(DiagnosticLogRecord diagnostic, string json, CancellationToken ct)
        => _diagnosticsRecorder is null
            ? Task.CompletedTask
            : _diagnosticsRecorder.RecordDiagnosticAsync(diagnostic, json, ct);
}
