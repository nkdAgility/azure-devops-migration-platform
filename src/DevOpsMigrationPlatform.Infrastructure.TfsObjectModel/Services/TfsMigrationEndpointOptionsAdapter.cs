using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;

/// <summary>
/// Lightweight adapter that wraps a resolved <see cref="OrganisationEndpoint"/> as a
/// <see cref="MigrationEndpointOptions"/> so it can be passed to
/// <see cref="TfsWorkItemQueryWindowStrategy"/> which still accepts
/// <see cref="MigrationEndpointOptions"/> (D-004 deferred alignment).
/// </summary>
internal sealed class TfsMigrationEndpointOptionsAdapter : MigrationEndpointOptions
{
    private readonly OrganisationEndpoint _endpoint;

    public TfsMigrationEndpointOptionsAdapter(OrganisationEndpoint endpoint)
    {
        _endpoint = endpoint;
        Type = endpoint.Type;
    }

    public override string GetResolvedUrl() => _endpoint.ResolvedUrl;
    public override string GetEndpointUrl() => _endpoint.ResolvedUrl;
}
