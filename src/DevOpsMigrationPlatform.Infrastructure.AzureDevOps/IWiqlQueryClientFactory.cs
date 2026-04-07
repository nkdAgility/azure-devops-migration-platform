using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Creates an <see cref="IWiqlQueryClient"/> for a given organisation URL and PAT,
/// keeping connection concerns separate from the windowing strategy logic.
/// </summary>
public interface IWiqlQueryClientFactory
{
    Task<IWiqlQueryClient> CreateAsync(
        string url,
        string pat,
        CancellationToken cancellationToken = default);
}
