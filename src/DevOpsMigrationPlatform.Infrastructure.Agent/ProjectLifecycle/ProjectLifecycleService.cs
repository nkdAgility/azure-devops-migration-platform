// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;

/// <summary>
/// Connector-agnostic project lifecycle orchestration with connector-specific actions delegated
/// to <see cref="IProjectLifecycleProvider"/>.
/// </summary>
public sealed class ProjectLifecycleService : IProjectLifecycleService
{
    private readonly IReadOnlyDictionary<string, Type>? _implementationTypes;
    private readonly IServiceProvider? _serviceProvider;
    private readonly IProjectLifecycleProvider? _singleProvider;
    private readonly IProjectLifecycleNameGenerator _nameGenerator;
    private readonly ILogger _logger;

    [ActivatorUtilitiesConstructor]
    public ProjectLifecycleService(
        IProjectLifecycleNameGenerator nameGenerator,
        IEnumerable<KeyedProjectLifecycleProvider> registrations,
        IServiceProvider serviceProvider,
        ILogger<ProjectLifecycleService> logger)
    {
        _nameGenerator = nameGenerator ?? throw new ArgumentNullException(nameof(nameGenerator));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
            dict[registration.Key] = registration.ServiceType;
        _implementationTypes = dict;
    }

    public ProjectLifecycleService(
        IProjectLifecycleNameGenerator nameGenerator,
        IProjectLifecycleProvider provider,
        ILogger logger)
    {
        _nameGenerator = nameGenerator ?? throw new ArgumentNullException(nameof(nameGenerator));
        _singleProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProjectLifecycleRecord> CreateAsync(ProjectLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        var projectName = ResolveProjectName(context);

        try
        {
            await ResolveProvider(context.ConnectorType).CreateActionAsync(context, projectName, cancellationToken);
            _logger.LogInformation(
                "Lifecycle create requested for connector {ConnectorType} project {ProjectName} runId={RunId}.",
                context.ConnectorType,
                projectName,
                context.RunId);

            return new ProjectLifecycleRecord
            {
                RunId = context.RunId,
                ConnectorType = context.ConnectorType,
                ProjectName = projectName,
                ProjectOwnedByRun = true,
                CreateResult = ProjectLifecycleCreateResult.Succeeded,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                TeardownResult = ProjectLifecycleTeardownResult.Skipped,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Lifecycle create failed for connector {ConnectorType} project {ProjectName} runId={RunId}.",
                context.ConnectorType,
                projectName,
                context.RunId);

            return new ProjectLifecycleRecord
            {
                RunId = context.RunId,
                ConnectorType = context.ConnectorType,
                ProjectName = projectName,
                ProjectOwnedByRun = false,
                CreateResult = ProjectLifecycleCreateResult.Failed,
                CreateFailureReason = ex.Message,
                TeardownResult = ProjectLifecycleTeardownResult.Skipped,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public async Task<ProjectLifecycleRecord> TeardownAsync(ProjectLifecycleRecord record, CancellationToken cancellationToken = default)
    {
        if (record is null)
            throw new ArgumentNullException(nameof(record));

        if (!record.ProjectOwnedByRun)
        {
            return new ProjectLifecycleRecord
            {
                RunId = record.RunId,
                ConnectorType = record.ConnectorType,
                ProjectName = record.ProjectName,
                ProjectOwnedByRun = record.ProjectOwnedByRun,
                CreateResult = record.CreateResult,
                CreateFailureReason = record.CreateFailureReason,
                CreatedAtUtc = record.CreatedAtUtc,
                TeardownResult = ProjectLifecycleTeardownResult.Failed,
                TeardownBlockingReason = "Refused to tear down project that is not owned by the current run.",
                PartialCleanupDetail = record.PartialCleanupDetail,
                TeardownAttemptedAtUtc = DateTimeOffset.UtcNow,
                TeardownLatency = record.TeardownLatency,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };
        }

        var started = DateTimeOffset.UtcNow;
        try
        {
            await ResolveProvider(record.ConnectorType).TeardownActionAsync(record, cancellationToken);
            _logger.LogInformation(
                "Lifecycle teardown requested for connector {ConnectorType} project {ProjectName} runId={RunId}.",
                record.ConnectorType,
                record.ProjectName,
                record.RunId);

            return new ProjectLifecycleRecord
            {
                RunId = record.RunId,
                ConnectorType = record.ConnectorType,
                ProjectName = record.ProjectName,
                ProjectOwnedByRun = record.ProjectOwnedByRun,
                CreateResult = record.CreateResult,
                CreateFailureReason = record.CreateFailureReason,
                CreatedAtUtc = record.CreatedAtUtc,
                TeardownResult = ProjectLifecycleTeardownResult.Succeeded,
                TeardownAttemptedAtUtc = DateTimeOffset.UtcNow,
                TeardownLatency = DateTimeOffset.UtcNow - started,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new ProjectLifecycleRecord
            {
                RunId = record.RunId,
                ConnectorType = record.ConnectorType,
                ProjectName = record.ProjectName,
                ProjectOwnedByRun = record.ProjectOwnedByRun,
                CreateResult = record.CreateResult,
                CreateFailureReason = record.CreateFailureReason,
                CreatedAtUtc = record.CreatedAtUtc,
                TeardownResult = ProjectLifecycleTeardownResult.Failed,
                TeardownBlockingReason = ex.Message,
                TeardownAttemptedAtUtc = DateTimeOffset.UtcNow,
                TeardownLatency = DateTimeOffset.UtcNow - started,
                RecordedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public async Task<ProjectLifecycleRecord> ExecuteWithGuaranteedTeardownAsync(
        ProjectLifecycleContext context,
        Func<ProjectLifecycleRecord, CancellationToken, Task> testAction,
        CancellationToken cancellationToken = default)
    {
        if (testAction is null)
            throw new ArgumentNullException(nameof(testAction));

        var record = await CreateAsync(context, cancellationToken);
        if (record.CreateResult == ProjectLifecycleCreateResult.Failed)
            return record;

        try
        {
            await testAction(record, cancellationToken);
        }
        finally
        {
            record = await TeardownAsync(record, cancellationToken);
        }

        return record;
    }

    private string ResolveProjectName(ProjectLifecycleContext context)
    {
        return string.IsNullOrWhiteSpace(context.ProjectName)
            ? _nameGenerator.Generate(context.RunId, context.ConnectorType, context.NamePrefix)
            : context.ProjectName;
    }

    private IProjectLifecycleProvider ResolveProvider(string connectorType)
    {
        if (_singleProvider is not null)
            return _singleProvider;

        if (string.IsNullOrWhiteSpace(connectorType))
            throw new InvalidOperationException("Project lifecycle connector type was not provided.");

        if (_implementationTypes is null || _serviceProvider is null)
            throw new InvalidOperationException("Project lifecycle implementation registry is unavailable.");

        if (!_implementationTypes.TryGetValue(connectorType, out var serviceType))
            throw new InvalidOperationException(
                $"No project lifecycle implementation is registered for endpoint type '{connectorType}'. " +
                "Register one with AddProjectLifecycleProvider(key, provider).");

        return (IProjectLifecycleProvider)_serviceProvider.GetRequiredService(serviceType);
    }
}

/// <summary>
/// Registration descriptor for a keyed <see cref="IProjectLifecycleProvider"/>.
/// </summary>
public sealed record KeyedProjectLifecycleProvider(string Key, Type ServiceType);
