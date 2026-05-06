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
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

/// <summary>
/// Pure <see cref="ICapture"/> implementation for dependency discovery.
/// Captures per-project dependency data (cross-project work item links) via
/// <see cref="IDependencyOrchestrator.CaptureProjectAsync"/>, writing a per-project
/// <c>discovery/{orgSlug}/{projectSlug}/dependencies.csv</c> artefact that the
/// <c>analyse.dependencies.*</c> fan-in task later consolidates.
/// <para>
/// This is NOT an <see cref="Abstractions.Agent.Modules.IModule"/>. It is registered directly as
/// <c>ICapture</c> in DI and included in <c>captureHandlersByName</c> only for connectors that
/// support dependency discovery (ADO and Simulated). TFS agents do NOT register this type.
/// </para>
/// </summary>
public sealed class DependencyCapture : ICapture
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Discovery);

    private readonly IDependencyDiscoveryServiceFactory _dependencyFactory;
    private readonly IDependencyOrchestrator _orchestrator;
    private readonly ILogger<DependencyCapture> _logger;
    private readonly IPlatformMetrics? _metrics;

    /// <summary>Name used to match <c>capture.dependencies.{org}.{project}</c> task IDs.</summary>
    public string Name => "dependencies";

    public DependencyCapture(
        IDependencyDiscoveryServiceFactory dependencyFactory,
        IDependencyOrchestrator orchestrator,
        ILogger<DependencyCapture> logger,
        IPlatformMetrics? metrics = null)
    {
        _dependencyFactory = dependencyFactory ?? throw new ArgumentNullException(nameof(dependencyFactory));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task CaptureAsync(InventoryContext context, CancellationToken ct)
    {
        var orgUrl = context.SourceEndpoint?.ResolvedUrl ?? string.Empty;
        var project = context.Project;

        using var activity = s_activitySource.StartActivity("capture.dependencies.project");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("organisation.url", orgUrl);
        activity?.SetTag("project.name", project);
        activity?.SetTag("module", Name);

        _logger.LogInformation(
            "[Dependencies] CaptureAsync starting for project {Project} in org {OrgUrl}.",
            project, orgUrl);

        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Capture.Started",
            Message = $"Capturing dependency links for {orgUrl}/{project}",
            Timestamp = DateTimeOffset.UtcNow
        });

        try
        {
            var dependencyService = _dependencyFactory.CreateForProject(
                context.Organisations,
                orgUrl,
                project,
                context.Policies);

            await _orchestrator.CaptureProjectAsync(
                dependencyService, context, context.Policies, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[Dependencies] CaptureAsync complete for project {Project} in org {OrgUrl}.",
                project, orgUrl);

            context.ProgressSink?.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = "Capture.Complete",
                Message = $"Dependency capture complete for {orgUrl}/{project}",
                Timestamp = DateTimeOffset.UtcNow,
                Metrics = new JobMetrics
                {
                    Discovery = new DiscoveryCounters
                    {
                        Dependencies = new DependencyCounters()
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Dependencies] CaptureAsync cancelled for project {Project}.", project);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Dependencies] CaptureAsync failed for project {Project} in org {OrgUrl}.",
                project, orgUrl);
            throw;
        }
    }
}
