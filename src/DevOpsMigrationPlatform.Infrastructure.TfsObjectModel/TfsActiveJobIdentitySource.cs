using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;

using AgentIdentityDescriptor = DevOpsMigrationPlatform.Abstractions.Agent.Identity.IdentityDescriptor;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// <see cref="IIdentitySource"/> adapter for the TFS agent.
/// Delegates to the <see cref="IIdentitySource"/> from the currently active
/// <see cref="TfsJobServices"/> held in <see cref="ActiveTfsJobServices"/>.
/// </summary>
public sealed class TfsActiveJobIdentitySource : IIdentitySource
{
    private readonly ActiveTfsJobServices _activeServices;

    public TfsActiveJobIdentitySource(ActiveTfsJobServices activeServices)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentIdentityDescriptor> EnumerateIdentitiesAsync(
        MigrationEndpointOptions endpoint,
        string projectName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var source = _activeServices.Require().IdentitySource;
        await foreach (var descriptor in source.EnumerateIdentitiesAsync(endpoint, projectName, cancellationToken).ConfigureAwait(false))
            yield return descriptor;
    }
}
