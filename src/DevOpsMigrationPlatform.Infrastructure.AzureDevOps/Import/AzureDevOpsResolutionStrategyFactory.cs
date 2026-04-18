using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Import;

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
        string project,
        string accessToken,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(target);

        // Simulated targets need no provenance lookup — return a no-op strategy.
        if (target is SimulatedWorkItemImportTarget)
            return new NullResolutionStrategy();

        if (string.IsNullOrEmpty(options.Strategy))
            throw new InvalidOperationException(
                "WorkItemResolutionStrategy.strategy must be configured for import jobs. " +
                "Supported values: \"TargetField\", \"TargetHyperlink\".");

        // Both ADO strategies require a live WIT client — create it once.
        // orgUrl is derived from the target; we accept accessToken from the import context.
        if (target is not AzureDevOpsWorkItemImportTarget adoTarget)
            throw new InvalidOperationException(
                $"AzureDevOpsResolutionStrategyFactory requires an AzureDevOpsWorkItemImportTarget " +
                $"but received {target.GetType().Name}.");

        var orgUrl = adoTarget.OrganisationUrl;
        var endpoint = new OrganisationEndpoint
        {
            ResolvedUrl = orgUrl,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = Abstractions.Options.AuthenticationType.Pat,
                ResolvedAccessToken = accessToken
            }
        };
        var witClient = await _clientFactory.CreateWorkItemClientAsync(endpoint, ct).ConfigureAwait(false);

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
