#if !NET481
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;

/// <summary>
/// Placeholder implementation of <see cref="IClassificationTreeReader"/> for the ADO REST connector.
/// Concrete connector implementations override the virtual methods to provide actual HTTP calls.
/// </summary>
public class AzureDevOpsClassificationTreeReader : IClassificationTreeReader
{
    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<string> EnumerateAreaNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <inheritdoc/>
    public virtual async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
#endif
