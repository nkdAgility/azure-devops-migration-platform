using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Factory for creating <see cref="AzureDevOpsWorkItemCommentSource"/> instances per export job.
/// Carries the organization URL, project, and PAT from the job context.
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
    public IWorkItemCommentSource Create(string organisationUrl, string project, string pat)
    {
        if (string.IsNullOrWhiteSpace(organisationUrl))
            throw new ArgumentException("Organization URL is required.", nameof(organisationUrl));
        if (string.IsNullOrWhiteSpace(project))
            throw new ArgumentException("Project is required.", nameof(project));
        if (pat == null)
            throw new ArgumentNullException(nameof(pat));

        var logger = _loggerFactory.CreateLogger<AzureDevOpsWorkItemCommentSource>();

        return new AzureDevOpsWorkItemCommentSource(
            _clientFactory,
            organisationUrl,
            project,
            pat,
            logger);
    }
}
