// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Operations;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle;

/// <summary>
/// Team Foundation Server-specific lifecycle actions.
/// </summary>
public sealed class TfsProjectLifecycleProvider : IProjectLifecycleProvider
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);
    private readonly IProjectProcessService? _processService;
    private readonly Func<ProjectLifecycleContext, CancellationToken, Task>? _createAction;
    private readonly Func<ProjectLifecycleRecord, CancellationToken, Task>? _teardownAction;
    private readonly Dictionary<string, OrganisationEndpoint> _endpointByRecordKey = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    [ActivatorUtilitiesConstructor]
    public TfsProjectLifecycleProvider(IProjectProcessService processService)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
    }

    public TfsProjectLifecycleProvider()
    {
    }

    internal TfsProjectLifecycleProvider(
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
            await _createAction(WithProjectName(context, projectName), cancellationToken).ConfigureAwait(false);
            return;
        }

        using var connection = CreateConnection(context.Endpoint);
        var projectClient = await connection.GetClientAsync<ProjectHttpClient>(cancellationToken).ConfigureAwait(false);
        var operationsClient = await connection.GetClientAsync<OperationsHttpClient>(cancellationToken).ConfigureAwait(false);
        var processTypeId = _processService is not null
            ? await _processService.ResolveProcessTypeIdAsync(context, cancellationToken).ConfigureAwait(false)
            : KnownProcessIds.Agile;

        var existing = await TryGetProjectAsync(projectClient, projectName, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.State != ProjectState.Deleted)
            throw new InvalidOperationException($"TFS project '{projectName}' already exists.");

        var queued = await projectClient.QueueCreateProject(
            BuildTeamProjectRequest(projectName, processTypeId),
            userState: null).ConfigureAwait(false);
        await WaitForOperationCompletionAsync(operationsClient, queued, "create", projectName, cancellationToken).ConfigureAwait(false);
        await WaitForProjectStateAsync(projectClient, projectName, ProjectState.WellFormed, cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _endpointByRecordKey[BuildRecordKey(context.RunId, projectName)] = context.Endpoint;
        }
    }

    public async Task TeardownActionAsync(ProjectLifecycleRecord record, CancellationToken cancellationToken)
    {
        if (_teardownAction is not null)
        {
            await _teardownAction(record, cancellationToken).ConfigureAwait(false);
            return;
        }

        OrganisationEndpoint endpoint;
        lock (_sync)
        {
            if (!_endpointByRecordKey.TryGetValue(BuildRecordKey(record.RunId, record.ProjectName), out endpoint!))
                throw new InvalidOperationException(
                    $"No endpoint context was captured for project lifecycle record runId='{record.RunId}' project='{record.ProjectName}'.");
        }

        using var connection = CreateConnection(endpoint);
        var projectClient = await connection.GetClientAsync<ProjectHttpClient>(cancellationToken).ConfigureAwait(false);
        var operationsClient = await connection.GetClientAsync<OperationsHttpClient>(cancellationToken).ConfigureAwait(false);
        var existing = await TryGetProjectAsync(projectClient, record.ProjectName, cancellationToken).ConfigureAwait(false);
        if (existing is null || existing.State == ProjectState.Deleted)
            return;

        var queued = await projectClient.QueueDeleteProject(existing.Id, hardDelete: false, userState: null).ConfigureAwait(false);
        await WaitForOperationCompletionAsync(operationsClient, queued, "delete", record.ProjectName, cancellationToken).ConfigureAwait(false);
        await WaitForProjectDeletionAsync(projectClient, record.ProjectName, cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _endpointByRecordKey.Remove(BuildRecordKey(record.RunId, record.ProjectName));
        }
    }

    private static VssConnection CreateConnection(OrganisationEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.ResolvedUrl))
            throw new InvalidOperationException("TFS project lifecycle endpoint URL was not provided.");

        VssCredentials credentials = endpoint.Authentication.Type == AuthenticationType.AccessToken &&
                                     !string.IsNullOrWhiteSpace(endpoint.Authentication.ResolvedAccessToken)
            ? new VssCredentials(new VssBasicCredential(string.Empty, endpoint.Authentication.ResolvedAccessToken))
            : new VssClientCredentials(true);

        return new VssConnection(new Uri(endpoint.ResolvedUrl), credentials);
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
            throw new InvalidOperationException($"TFS {action} did not return a valid operation reference for project '{projectName}'.");

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
                        $"TFS {action} operation failed for project '{projectName}': " +
                        $"{operation.DetailedMessage ?? operation.ResultMessage ?? operation.Url}");
                case OperationStatus.Cancelled:
                    throw new InvalidOperationException($"TFS {action} operation was cancelled for project '{projectName}'.");
            }

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for TFS {action} operation for project '{projectName}'.");
    }

    private static bool IsNotFound(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("TF400324", StringComparison.OrdinalIgnoreCase) >= 0
               || message.IndexOf("VS800075", StringComparison.OrdinalIgnoreCase) >= 0;
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

    private static string BuildRecordKey(string runId, string projectName) => $"{runId}::{projectName}";
}
