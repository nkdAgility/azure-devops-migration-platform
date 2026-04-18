using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Azure DevOps–aware <see cref="IWorkItemResolutionStrategyFactory"/>.
/// Creates the appropriate <see cref="IWorkItemResolutionStrategy"/> implementation
/// based on the configured <c>WorkItemResolutionStrategyOptions.Strategy</c> value:
/// <list type="bullet">
///   <item><c>"TargetField"</c> — <see cref="TargetFieldResolutionStrategy"/></item>
///   <item><c>"TargetHyperlink"</c> — <see cref="TargetHyperlinkResolutionStrategy"/></item>
///   <item>Any other or empty value — throws <see cref="InvalidOperationException"/>.</item>
/// </list>
/// A <c>WorkItemResolutionStrategy</c> extension with a valid <c>strategy</c> parameter
/// is mandatory for every import job.
/// </summary>
public sealed class AzureDevOpsResolutionStrategyFactory : IWorkItemResolutionStrategyFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public AzureDevOpsResolutionStrategyFactory(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemResolutionStrategy> CreateAsync(
        WorkItemResolutionStrategyOptions options,
        IWorkItemImportTarget target,
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(endpoint);

        if (string.IsNullOrEmpty(options.Strategy))
            throw new InvalidOperationException(
                "WorkItemResolutionStrategy.strategy must be configured for import jobs. " +
                "Supported values: \"TargetField\", \"TargetHyperlink\".");

        if (target is not AzureDevOpsWorkItemImportTarget adoTarget)
            throw new InvalidOperationException(
                $"AzureDevOpsResolutionStrategyFactory requires an AzureDevOpsWorkItemImportTarget " +
                $"but received {target.GetType().Name}.");

        var orgEndpoint = endpoint.ToOrganisationEndpoint();
        var project = endpoint.GetProject();
        var witClient = await _clientFactory.CreateWorkItemClientAsync(orgEndpoint, ct).ConfigureAwait(false);

        return options.Strategy switch
        {
            "TargetField" => new TargetFieldResolutionStrategy(witClient, target, project, options.FieldName),
            "TargetHyperlink" => new TargetHyperlinkResolutionStrategy(witClient, target, project, options.UrlPattern),
            _ => throw new InvalidOperationException(
                $"Unknown WorkItemResolutionStrategy.strategy value \"{options.Strategy}\". " +
                $"Supported values: \"TargetField\", \"TargetHyperlink\".")
        };
    }
}
