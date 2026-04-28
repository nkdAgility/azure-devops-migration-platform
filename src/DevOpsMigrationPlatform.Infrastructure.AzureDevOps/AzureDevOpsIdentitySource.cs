using System;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps REST API implementation of <see cref="IIdentitySource"/>.
/// Enumerates users and groups for a project using the Azure DevOps Graph API.
/// </summary>
public sealed class AzureDevOpsIdentitySource : IIdentitySource
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ILogger<AzureDevOpsIdentitySource> _logger;

    public AzureDevOpsIdentitySource(
        IAzureDevOpsClientFactory clientFactory,
        ILogger<AzureDevOpsIdentitySource> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
        string projectName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Identities/ADO] Enumerating identities for project '{Project}'.", projectName);

        // Full identity enumeration via Azure DevOps Graph API requires the organisation URL
        // which is only available from the connector endpoint at export time.
        // The current IIdentitySource contract passes only the project name.
        // This implementation logs a warning and returns no identities.
        // A future iteration should extend IIdentitySource to accept OrganisationEndpoint context.
        _logger.LogWarning(
            "[Identities/ADO] AzureDevOpsIdentitySource: full Graph API identity enumeration requires " +
            "OrganisationEndpoint context. This implementation returns no identities. " +
            "Extend IIdentitySource to accept endpoint context in a future iteration.");

        yield break;
    }
}
