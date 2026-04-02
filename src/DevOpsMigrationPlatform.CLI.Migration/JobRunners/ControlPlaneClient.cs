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
public sealed class ControlPlaneClient : IJobRunner
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
}
