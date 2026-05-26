// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Operations;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.ProjectLifecycle;

/// <summary>
/// Azure DevOps-specific project lifecycle actions.
/// </summary>
public sealed class AzureDevOpsProjectLifecycleProvider : IProjectLifecycleProvider
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<string, OrganisationEndpoint> _createdEndpointByRecordKey = new(StringComparer.Ordinal);

    private readonly IAzureDevOpsClientFactory? _clientFactory;
    private readonly IProjectProcessService? _processService;
    private readonly Func<ProjectLifecycleContext, CancellationToken, Task>? _createAction;
    private readonly Func<ProjectLifecycleRecord, CancellationToken, Task>? _teardownAction;

    [ActivatorUtilitiesConstructor]
    public AzureDevOpsProjectLifecycleProvider(
        IAzureDevOpsClientFactory clientFactory,
        IProjectProcessService processService)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
    }

    internal AzureDevOpsProjectLifecycleProvider(
        Func<ProjectLifecycleContext, CancellationToken, Task>? createAction = null,
        Func<ProjectLifecycleRecord, CancellationToken, Task>? teardownAction = null)
    {
        _createAction = createAction;
        _teardownAction = teardownAction;
    }

    public async Task CreateActionAsync(ProjectLifecycleContext context, string projectName, CancellationToken cancellationToken)
    {
        if (_createAction is not null)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await _createAction(WithProjectName(context, projectName), cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_clientFactory is null)
            throw new InvalidOperationException("Azure DevOps project lifecycle provider requires an SDK client factory.");
        if (_processService is null)
            throw new InvalidOperationException("Azure DevOps project lifecycle provider requires a process service.");

        var endpoint = context.Endpoint ?? throw new InvalidOperationException("Project lifecycle endpoint was not provided.");
        var projectClient = await _clientFactory.CreateProjectClientAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var operationsClient = await _clientFactory.CreateOperationsClientAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var processTypeId =
            await _processService.ResolveProcessTypeIdAsync(context, cancellationToken).ConfigureAwait(false);

        var existing = await TryGetProjectAsync(projectClient, projectName, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.State != ProjectState.Deleted)
            throw new InvalidOperationException($"Azure DevOps project '{projectName}' already exists.");

        await ExecuteWithRetryAsync(async () =>
        {
            var queued = await projectClient.QueueCreateProject(
                BuildTeamProjectRequest(projectName, processTypeId),
                userState: null).ConfigureAwait(false);

            await WaitForOperationCompletionAsync(operationsClient, queued, "create", projectName, cancellationToken).ConfigureAwait(false);
            await WaitForProjectStateAsync(projectClient, projectName, ProjectState.WellFormed, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        _createdEndpointByRecordKey[BuildRecordKey(context.RunId, projectName)] = context.Endpoint;
    }

    public async Task TeardownActionAsync(ProjectLifecycleRecord record, CancellationToken cancellationToken)
    {
        if (_teardownAction is not null)
        {
            await _teardownAction(record, cancellationToken);
            return;
        }

        if (_clientFactory is null)
            throw new InvalidOperationException("Azure DevOps project lifecycle provider requires an SDK client factory.");

        var key = BuildRecordKey(record.RunId, record.ProjectName);
        if (!_createdEndpointByRecordKey.TryGetValue(key, out var endpoint))
            throw new InvalidOperationException(
                $"No endpoint context was captured for project lifecycle record runId='{record.RunId}' project='{record.ProjectName}'.");

        var projectClient = await _clientFactory.CreateProjectClientAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var operationsClient = await _clientFactory.CreateOperationsClientAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var existing = await TryGetProjectAsync(projectClient, record.ProjectName, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.State == ProjectState.Deleted)
            return;

        var queued = await projectClient.QueueDeleteProject(existing.Id, hardDelete: false, userState: null).ConfigureAwait(false);
        await WaitForOperationCompletionAsync(operationsClient, queued, "delete", record.ProjectName, cancellationToken).ConfigureAwait(false);
        await WaitForProjectDeletionAsync(projectClient, record.ProjectName, cancellationToken).ConfigureAwait(false);
        _createdEndpointByRecordKey.TryRemove(key, out _);
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
            ProcessName = context.ProcessName,
            Endpoint = context.Endpoint
        };
    }

    private static TeamProject BuildTeamProjectRequest(string projectName, string processTypeId)
    {
        return new TeamProject
        {
            Name = projectName,
            Capabilities = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                [TeamProjectCapabilitiesConstants.VersionControlCapabilityName] =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [TeamProjectCapabilitiesConstants.VersionControlCapabilityAttributeName] = "Git"
                    },
                [TeamProjectCapabilitiesConstants.ProcessTemplateCapabilityName] =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [TeamProjectCapabilitiesConstants.ProcessTemplateCapabilityTemplateTypeIdAttributeName] = processTypeId
                    }
            }
        };
    }

    private static async Task<TeamProject?> TryGetProjectAsync(
        ProjectHttpClient projectClient,
        string projectName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await projectClient.GetProject(
                projectName,
                includeCapabilities: null,
                includeHistory: false,
                userState: null).ConfigureAwait(false);
        }
        catch (VssServiceException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    private static async Task WaitForProjectStateAsync(
        ProjectHttpClient projectClient,
        string projectName,
        ProjectState expectedState,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + OperationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = await TryGetProjectAsync(projectClient, projectName, cancellationToken).ConfigureAwait(false);
            if (project is not null && project.State == expectedState)
                return;

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for project '{projectName}' to reach state '{expectedState}'.");
    }

    private static async Task WaitForProjectDeletionAsync(
        ProjectHttpClient projectClient,
        string projectName,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + OperationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = await TryGetProjectAsync(projectClient, projectName, cancellationToken).ConfigureAwait(false);
            if (project is null || project.State == ProjectState.Deleted)
                return;

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for project '{projectName}' to be deleted.");
    }

    private static async Task WaitForOperationCompletionAsync(
        OperationsHttpClient operationsClient,
        OperationReference operationReference,
        string action,
        string projectName,
        CancellationToken cancellationToken)
    {
        if (operationReference is null || operationReference.Id == Guid.Empty)
            throw new InvalidOperationException($"Azure DevOps {action} did not return a valid operation reference for project '{projectName}'.");

        var deadline = DateTimeOffset.UtcNow + OperationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operation = await operationsClient.GetOperationAsync(
                operationReference,
                userState: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            switch (operation.Status)
            {
                case OperationStatus.Succeeded:
                    return;
                case OperationStatus.Failed:
                    throw new InvalidOperationException(
                        $"Azure DevOps {action} operation failed for project '{projectName}': " +
                        $"{operation.DetailedMessage ?? operation.ResultMessage ?? operation.Url}");
                case OperationStatus.Cancelled:
                    throw new InvalidOperationException($"Azure DevOps {action} operation was cancelled for project '{projectName}'.");
            }

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for Azure DevOps {action} operation for project '{projectName}'.");
    }

    private static bool IsNotFound(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
               || message.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || message.Contains("TF400324", StringComparison.OrdinalIgnoreCase)
               || message.Contains("VS800075", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRecordKey(string runId, string projectName) => $"{runId}::{projectName}";
}
