using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated;

/// <summary>
/// Simulated <see cref="IIdentitySource"/> that produces a deterministic set of
/// identity descriptors for use in tests and simulated migration runs.
/// </summary>
public sealed class SimulatedIdentitySource : IIdentitySource
{
    private static readonly IdentityDescriptor[] s_identities = new[]
    {
        new IdentityDescriptor("desc-alice", "Alice Smith", "alice@simulated.example.com", "User", "Simulated", true),
        new IdentityDescriptor("desc-bob", "Bob Jones", "bob@simulated.example.com", "User", "Simulated", true),
        new IdentityDescriptor("desc-carol", "Carol Taylor", "carol@simulated.example.com", "User", "Simulated", true),
        new IdentityDescriptor("desc-dave", "Dave Brown", "dave@simulated.example.com", "User", "Simulated", true),
        new IdentityDescriptor("desc-eve", "Eve White", "eve@simulated.example.com", "User", "Simulated", false),
        new IdentityDescriptor("desc-group-devs", "Developers", "devs@simulated.example.com", "Group", "Simulated", true),
        new IdentityDescriptor("desc-group-admins", "Administrators", "admins@simulated.example.com", "Group", "Simulated", true),
    };

    /// <inheritdoc/>
    public async IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
        string projectName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var identity in s_identities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return identity;
            await System.Threading.Tasks.Task.Yield();
        }
    }
}
