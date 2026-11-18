// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// Ends jobs the Control Plane fails before any agent ran them (#169, ADR-0035): a job whose
/// dependency failed, or a job no connected agent can serve. It periodically runs the
/// unservable-job check, and for every such failure publishes the reason on the job's progress
/// stream and closes the stream as failed, so a following CLI shows why and stops waiting.
/// Routing only: it never runs, plans or inspects migration work (ADR-0004).
/// </summary>
public sealed class UnservableJobMonitor : BackgroundService
{
    private readonly IJobStore _jobStore;
    private readonly JobProgressStore _progressStore;
    private readonly DiagnosticLogStore _diagnosticStore;
    private readonly JobRoutingOptions _options;
    private readonly ILogger<UnservableJobMonitor>? _logger;

    public UnservableJobMonitor(
        IJobStore jobStore,
        JobProgressStore progressStore,
        DiagnosticLogStore diagnosticStore,
        IOptions<JobRoutingOptions> options,
        ILogger<UnservableJobMonitor>? logger = null)
    {
        _jobStore = jobStore;
        _progressStore = progressStore;
        _diagnosticStore = diagnosticStore;
        _options = options.Value;
        _logger = logger;
        _jobStore.JobFailedWithoutRunning += OnJobFailedWithoutRunning;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.CheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                // Failures are published through JobFailedWithoutRunning.
                _jobStore.FailUnservableJobs(_options.UnservableJobTimeout);
                _jobStore.FailSilentLeases(_options.SilentLeaseTimeout);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutting down.
        }
    }

    private void OnJobFailedWithoutRunning(Guid jobId, string reason)
    {
        _progressStore.Append(jobId, new ProgressEvent
        {
            // Next sequence for this job, so the stream's after-close replay does not re-send it.
            EventSequence = _progressStore.GetMaxEventSequence(jobId) + 1,
            Module = "ControlPlane",
            Stage = "Routing",
            Message = reason,
            Timestamp = DateTimeOffset.UtcNow
        });
        _progressStore.CompleteJob(jobId, failed: true, failureReason: reason);
        _diagnosticStore.CompleteJob(jobId, failed: true);
        _logger?.LogWarning("[ControlPlane] Job {JobId} failed without running: {Reason}", jobId, reason);
    }

    public override void Dispose()
    {
        _jobStore.JobFailedWithoutRunning -= OnJobFailedWithoutRunning;
        base.Dispose();
    }
}
