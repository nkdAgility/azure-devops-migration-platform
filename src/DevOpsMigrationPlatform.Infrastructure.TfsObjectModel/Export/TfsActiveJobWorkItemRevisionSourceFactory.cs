using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Options;
namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;

/// <summary>
/// <see cref="IWorkItemRevisionSourceFactory"/> adapter for the TFS agent.
/// Returns the <see cref="IWorkItemRevisionSource"/> from the currently active
/// <see cref="TfsJobServices"/> held in <see cref="ActiveTfsJobServices"/>.
/// The TFS agent creates one <see cref="TfsJobServices"/> per job and stores it in
/// <see cref="ActiveTfsJobServices"/>; this factory simply reads the pre-created source.
/// Lifetime is managed by <see cref="TfsJobAgentWorker"/> — the source is valid for
/// the duration of the job and disposed in the worker's finally block.
/// </summary>
public sealed class TfsActiveJobWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly ActiveTfsJobServices _activeServices;

    public TfsActiveJobWorkItemRevisionSourceFactory(ActiveTfsJobServices activeServices)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
    }

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        var services = _activeServices.Require();
        return Task.FromResult(services.RevisionSource);
    }
}
