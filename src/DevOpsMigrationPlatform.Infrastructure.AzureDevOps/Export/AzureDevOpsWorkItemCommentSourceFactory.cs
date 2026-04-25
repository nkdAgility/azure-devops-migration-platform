using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;

/// <summary>
/// Factory for creating <see cref="AzureDevOpsWorkItemCommentSource"/> instances per export job.
/// Carries the organization endpoint and project from the job context.
/// Implements the generic <see cref="IWorkItemCommentSourceFactory"/> interface for use in Infrastructure-level services.
/// </summary>
public sealed class AzureDevOpsWorkItemCommentSourceFactory : IWorkItemCommentSourceFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public AzureDevOpsWorkItemCommentSourceFactory(
        IAzureDevOpsClientFactory clientFactory,
        ILoggerFactory loggerFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public IWorkItemCommentSource Create(MigrationEndpointOptions endpoint, string project)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));
        if (string.IsNullOrWhiteSpace(endpoint.GetResolvedUrl()))
            throw new ArgumentException("Organization URL is required.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(project))
            throw new ArgumentException("Project is required.", nameof(project));

        var orgEndpoint = endpoint.ToOrganisationEndpoint();
        var logger = _loggerFactory.CreateLogger<AzureDevOpsWorkItemCommentSource>();

        return new AzureDevOpsWorkItemCommentSource(
            _clientFactory,
            orgEndpoint,
            project,
            logger);
    }
}
