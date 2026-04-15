#if !NET481
using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Import;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemImportTarget"/> for offline or system-test scenarios
/// (target.type = "Simulated" in the scenario config).
/// </summary>
public sealed class SimulatedWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    /// <inheritdoc/>
    public Task<IWorkItemImportTarget> CreateAsync(
        string orgUrl,
        string project,
        string accessToken,
        CancellationToken ct)
    {
        IWorkItemImportTarget target = new SimulatedWorkItemImportTarget();
        return Task.FromResult(target);
    }
}
#endif
