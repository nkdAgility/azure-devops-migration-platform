// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Dependency analysis module that discovers cross-project and cross-organisation work item links.
/// Thin wrapper that resolves configuration, creates the service, and delegates all orchestration
/// logic to <see cref="DependencyOrchestrator"/>.
/// <para>
/// <strong>Module contract:</strong> Implements <see cref="IModule.ExportAsync"/> to perform
/// dependency discovery. Import and validation are not supported.
/// Has no dependencies — runs during Export phase after Inventory completes.
/// </para>
/// </summary>
public sealed class DependencyDiscoveryModule : IModule
{
    private readonly IDependencyDiscoveryServiceFactory _dependencyFactory;
    private readonly ILogger<DependencyDiscoveryModule> _logger;
    private readonly IDiscoveryMetrics? _metrics;
    private readonly IOptions<DiscoveryOptions>? _discoveryOptions;
    private readonly IDependencyOrchestrator _orchestrator;

    public string Name => "Dependencies";
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
    public bool SupportsExport => true;
    public bool SupportsImport => false;

    public DependencyDiscoveryModule(
        IDependencyDiscoveryServiceFactory dependencyFactory,
        ILogger<DependencyDiscoveryModule> logger,
        IDependencyOrchestrator orchestrator,
        IDiscoveryMetrics? metrics = null,
        IOptions<DiscoveryOptions>? discoveryOptions = null)
    {
        _dependencyFactory = dependencyFactory;
        _logger = logger;
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _metrics = metrics;
        _discoveryOptions = discoveryOptions;
    }

    /// <summary>
    /// Performs dependency discovery during the Export phase. Writes dependencies.csv.
    /// </summary>
    public Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        // Organisations come from context.Organisations (populated by the agent from migration-config.json).
        // Fall back to _discoveryOptions for backward compatibility in unit tests without a package.
        var organisations = context.Organisations.Count > 0
            ? context.Organisations.ToList()
            : (_discoveryOptions?.Value?.Organisations ?? new List<OrganisationEntry>())
                .Where(o => o.Enabled)
                .Select(o => new ScopedOrganisationEndpoint
                {
                    Endpoint = o.ToEndpointOptions(),
                    Projects = new List<string>(o.Projects)
                })
                .ToList<ScopedOrganisationEndpoint>();

        var policies = _discoveryOptions?.Value?.Policies is { } p
            ? new JobPolicies { MaxRetries = p.Retries.Max, MaxConcurrency = p.Throttle.MaxConcurrency, CheckpointIntervalSeconds = p.Checkpoints.Interval }
            : new JobPolicies();

        var dependencyService = _dependencyFactory.Create(organisations, policies);

        var checkpointIntervalSeconds = _discoveryOptions?.Value?.Policies?.Checkpoints?.Interval ?? 300;

        return _orchestrator.ExportAsync(dependencyService, context, organisations, policies, checkpointIntervalSeconds, ct);
    }

    /// <summary>
    /// Import is not supported for the Dependencies module.
    /// Dependencies is an analysis-only operation that runs during Export.
    /// </summary>
    public Task ImportAsync(ImportContext context, CancellationToken ct)
    {
        throw new NotSupportedException("Dependencies module does not support import operations. Dependencies runs only during the Export phase.");
    }

    /// <summary>
    /// Validation is not supported for the Dependencies module.
    /// </summary>
    public Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        return Task.CompletedTask; // No-op: Dependencies has no validation logic
    }

    // Expose static helpers for backward compatibility with tests that reference them directly.
    internal static string StripCsvRowsForProject(string csvContent, string orgUrl, string projectName, out int strippedCount)
        => DependencyOrchestrator.StripCsvRowsForProject(csvContent, orgUrl, projectName, out strippedCount);
}
