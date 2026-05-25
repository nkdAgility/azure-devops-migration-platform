// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.ProjectLifecycle;

/// <summary>
/// Azure DevOps-specific project lifecycle actions.
/// </summary>
public sealed class AzureDevOpsProjectLifecycleProvider : IProjectLifecycleProvider
{
    private readonly Func<ProjectLifecycleContext, CancellationToken, Task>? _createAction;
    private readonly Func<ProjectLifecycleRecord, CancellationToken, Task>? _teardownAction;

    public AzureDevOpsProjectLifecycleProvider(
        Func<ProjectLifecycleContext, CancellationToken, Task>? createAction = null,
        Func<ProjectLifecycleRecord, CancellationToken, Task>? teardownAction = null)
    {
        _createAction = createAction;
        _teardownAction = teardownAction;
    }

    public async Task CreateActionAsync(ProjectLifecycleContext context, string projectName, CancellationToken cancellationToken)
    {
        await ApplyReadinessDelayAsync(context, cancellationToken);
        await ExecuteWithRetryAsync(async () =>
        {
            if (_createAction is null)
                return;

            await _createAction(WithProjectName(context, projectName), cancellationToken);
        }, cancellationToken);
    }

    public async Task TeardownActionAsync(ProjectLifecycleRecord record, CancellationToken cancellationToken)
    {
        if (_teardownAction is not null)
            await _teardownAction(record, cancellationToken);
    }

    private static async Task ApplyReadinessDelayAsync(ProjectLifecycleContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Endpoint.ApiVersion))
            return;

        if (!context.Endpoint.ApiVersion.StartsWith("delay-ms:", StringComparison.OrdinalIgnoreCase))
            return;

        var delayValue = context.Endpoint.ApiVersion.Substring("delay-ms:".Length);
        if (int.TryParse(delayValue, out var delayMs) && delayMs > 0)
            await Task.Delay(delayMs, cancellationToken);
    }

    private static async Task ExecuteWithRetryAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }

        await operation();
    }

    private static bool IsTransient(Exception exception)
    {
        return exception is TimeoutException ||
               exception.Message.StartsWith("TRANSIENT", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectLifecycleContext WithProjectName(ProjectLifecycleContext context, string projectName)
    {
        if (string.Equals(context.ProjectName, projectName, StringComparison.Ordinal))
            return context;

        return new ProjectLifecycleContext
        {
            RunId = context.RunId,
            ConnectorType = context.ConnectorType,
            ProjectName = projectName,
            NamePrefix = context.NamePrefix,
            Endpoint = context.Endpoint
        };
    }
}
