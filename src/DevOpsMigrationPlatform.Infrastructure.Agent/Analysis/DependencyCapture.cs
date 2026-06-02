// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

/// <summary>
/// Pure <see cref="ICapture"/> implementation for per-project dependency discovery.
/// Captures cross-project work item links via <see cref="IDependencyOrchestrator.CaptureProjectAsync"/>,
/// writing a per-project <c>{org}/{project}/dependencies.csv</c> artefact that the
/// <c>analyse.dependencies.*</c> fan-in task later consolidates.
/// <para>
/// This is NOT an <see cref="IModule"/>. It is registered directly as <c>ICapture</c> in DI
/// and included in <c>captureHandlersByName</c> only for connectors that support dependency
/// discovery (ADO and Simulated). TFS agents must NOT register this type.
/// </para>
/// </summary>
public sealed class DependencyCapture : ICapture
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Discovery);

    private readonly IDependencyDiscoveryServiceFactory _dependencyFactory;
    private readonly IDependencyOrchestrator _orchestrator;
    private readonly ILogger<DependencyCapture> _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly IProgressSink? _progressSink;
    private readonly double _slowCaptureThresholdMs;

    /// <summary>
    /// Default duration (ms) above which a per-project capture is considered slow.
    /// Override via the <c>slowCaptureThresholdMs</c> constructor parameter (for tests).
    /// </summary>
    private const double DefaultSlowCaptureThresholdMs = 30_000; // 30 seconds

    /// <summary>
    /// The second dot-segment extracted from <c>capture.dependencies.{org}.{project}</c> task IDs.
    /// Matches by <see cref="StringComparer.OrdinalIgnoreCase"/> in <c>captureHandlersByName</c>.
    /// </summary>
    public string Name => "dependencies";

    public DependencyCapture(
        IDependencyDiscoveryServiceFactory dependencyFactory,
        IDependencyOrchestrator orchestrator,
        ILogger<DependencyCapture> logger,
        IPlatformMetrics? metrics = null,
        IProgressSink? progressSink = null,
        double slowCaptureThresholdMs = DefaultSlowCaptureThresholdMs)
    {
        _dependencyFactory = dependencyFactory ?? throw new ArgumentNullException(nameof(dependencyFactory));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _progressSink = progressSink;
        _slowCaptureThresholdMs = slowCaptureThresholdMs;
    }

    /// <inheritdoc />
    public async Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
    {
        var orgUrl = context.SourceEndpoint?.ResolvedUrl ?? string.Empty;
        var project = context.Project;
        var jobId = context.Job.JobId;

        var tags = MetricsTagList.Create(jobId, "capture", Name);

        // O-4: in-flight increment (decremented in finally)
        _metrics?.DependenciesCaptureInFlightIncrement(tags);

        var sw = Stopwatch.StartNew();

        // O-1: root span
        using var rootActivity = s_activitySource.StartActivity("dependency.capture");
        rootActivity?.SetTag(WellKnownTagNames.Job.Id, jobId);
        rootActivity?.SetTag(WellKnownTagNames.Organisation.Url, orgUrl);
        rootActivity?.SetTag(WellKnownTagNames.Organisation.ProjectName, project);
        rootActivity?.SetTag(WellKnownTagNames.Capture.HandlerName, "dependencies");

        // O-2: started
        _metrics?.DependenciesCaptureStarted(tags);

        // O-3: log start (customer-data scope: orgUrl, project)
        using (DataClassificationScope.Begin(DataClassification.Customer))
        {
            _logger.LogInformation(
                "Capture started for {Org}/{Project} via handler {Handler} (job {JobId})",
                orgUrl, project, Name, jobId);
        }

        // O-4: emit Capturing progress event
        (context.ProgressSink ?? _progressSink)?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Capturing",
            Message = $"Capturing dependency links for {orgUrl}/{project}",
            Timestamp = DateTimeOffset.UtcNow
        });

        try
        {
            IDependencyDiscoveryService dependencyService;

            // O-1: child span — create_service
            using (var createServiceActivity = s_activitySource.StartActivity("dependency.capture.create_service"))
            {
                createServiceActivity?.SetTag(WellKnownTagNames.Organisation.Url, orgUrl);
                createServiceActivity?.SetTag(WellKnownTagNames.Organisation.ProjectName, project);

                dependencyService = _dependencyFactory.CreateForProject(
                    context.Organisations,
                    orgUrl,
                    project,
                    context.Policies);
            }

            // O-1: child span — execute
            DependencyCounters counters;
            using (var executeActivity = s_activitySource.StartActivity("dependency.capture.execute"))
            {
                executeActivity?.SetTag(WellKnownTagNames.Organisation.Url, orgUrl);
                executeActivity?.SetTag(WellKnownTagNames.Organisation.ProjectName, project);

                counters = await _orchestrator.CaptureProjectAsync(
                    dependencyService, context, context.Policies, ct).ConfigureAwait(false);
            }

            // O-1: child span — write_csv (confirm output path)
            var orgFolder = PackagePathResolver.ExtractOrgFolderName(orgUrl);
            var sanitizedProject = PackagePathResolver.Sanitise(project);
            var outputPath = $"{orgFolder}/{sanitizedProject}/dependencies.csv";

            // O-3: debug log if file already exists (overwrite path)
            if (await context.Package.RequestIndexAsync(
                    new PackageIndexContext("dependencies.csv", Organisation: orgFolder, Project: sanitizedProject),
                    ct).ConfigureAwait(false) != null)
            {
                using (DataClassificationScope.Begin(DataClassification.Customer))
                {
                    _logger.LogDebug(
                        "CSV already exists at {OutputPath}, overwriting (job {JobId})",
                        outputPath, jobId);
                }
            }

            using (var writeCsvActivity = s_activitySource.StartActivity("dependency.capture.write_csv"))
            {
                writeCsvActivity?.SetTag(WellKnownTagNames.Organisation.Url, orgUrl);
                writeCsvActivity?.SetTag(WellKnownTagNames.Organisation.ProjectName, project);
                writeCsvActivity?.SetTag(WellKnownTagNames.Capture.OutputPath, outputPath);
            }

            sw.Stop();

            // O-2: completed + duration + items analysed
            _metrics?.DependenciesCaptureCompleted(tags);
            _metrics?.RecordDependenciesCaptureDuration(sw.Elapsed.TotalMilliseconds, tags);
            _metrics?.RecordWorkItemsAnalysed((int)counters.WorkItemsAnalysed, tags);

            var durationMs = sw.Elapsed.TotalMilliseconds;

            // O-3: log completion (customer-data scope: orgUrl, project, outputPath)
            using (DataClassificationScope.Begin(DataClassification.Customer))
            {
                _logger.LogInformation(
                    "Capture completed for {Org}/{Project} in {DurationMs}ms → {OutputPath} (job {JobId})",
                    orgUrl, project, durationMs, outputPath, jobId);
            }

            // O-3: warn if capture was slow
            if (durationMs > _slowCaptureThresholdMs)
            {
                using (DataClassificationScope.Begin(DataClassification.Customer))
                {
                    _logger.LogWarning(
                        "Dependency slow: {Dependency} took {DurationMs}ms > {ThresholdMs}ms (job {JobId})",
                        $"{orgUrl}/{project}", durationMs, _slowCaptureThresholdMs, jobId);
                }
            }

            // O-4: emit Captured progress event with real counters from orchestrator
            (context.ProgressSink ?? _progressSink)?.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = "Captured",
                Message = $"Dependency capture complete for {orgUrl}/{project}",
                Timestamp = DateTimeOffset.UtcNow,
                Metrics = new JobMetrics
                {
                    Discovery = new DiscoveryCounters
                    {
                        Dependencies = new DependencyCounters
                        {
                            WorkItemsAnalysed = counters.WorkItemsAnalysed,
                            ExternalLinksFound = counters.ExternalLinksFound,
                            CrossProjectLinks = counters.CrossProjectLinks,
                            CrossOrgLinks = counters.CrossOrgLinks
                        }
                    }
                }
            });

            return TaskExecutionResult.Completed();
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            using (DataClassificationScope.Begin(DataClassification.Customer))
            {
                _logger.LogWarning("[Dependencies] CaptureAsync cancelled for {Project} in {Org}.", project, orgUrl);
            }
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();

            // O-2: failed + duration
            _metrics?.DependenciesCaptureFailed(tags);
            _metrics?.RecordDependenciesCaptureDuration(sw.Elapsed.TotalMilliseconds, tags);

            // O-3: log error (customer-data scope: orgUrl, project)
            using (DataClassificationScope.Begin(DataClassification.Customer))
            {
                _logger.LogError(
                    "Capture failed for {Org}/{Project}: {ErrorType} {ErrorMessage} after {DurationMs}ms (job {JobId})",
                    orgUrl, project, ex.GetType().Name, ex.Message, sw.Elapsed.TotalMilliseconds, jobId);
            }

            // O-4: emit Failed progress event
            (context.ProgressSink ?? _progressSink)?.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = "Failed",
                Message = $"Dependency capture failed for {orgUrl}/{project}: {ex.Message}",
                Timestamp = DateTimeOffset.UtcNow
            });

            throw;
        }
        finally
        {
            // O-2: decrement in-flight (always)
            _metrics?.DependenciesCaptureInFlightDecrement(tags);
        }
    }
}
