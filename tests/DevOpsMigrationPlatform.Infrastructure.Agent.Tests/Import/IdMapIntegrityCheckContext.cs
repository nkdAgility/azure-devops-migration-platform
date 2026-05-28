// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared scenario state for ID Map Integrity Check step definitions.
/// </summary>
public class IdMapIntegrityCheckContext
{
    public Mock<IIdMapStore> MockIdMapStore { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemTarget> MockTarget { get; } = new(MockBehavior.Strict);

    /// <summary>Mappings seeded into the mock idmap.db.</summary>
    public List<IdMapEntry> ConfiguredMappings { get; } = new();

    /// <summary>Target IDs that exist in the target system.</summary>
    public HashSet<int> ExistingTargetIds { get; } = new();

    /// <summary>Result returned by CheckIntegrityAsync — stale/broken mappings.</summary>
    public IReadOnlyList<IdMapEntry>? IntegrityResult { get; set; }

    /// <summary>Indicates the pipeline continued after the integrity check.</summary>
    public bool PipelineContinued { get; set; }

    /// <summary>
    /// Sets up the <see cref="MockIdMapStore"/> so that <c>CheckIntegrityAsync</c> invokes
    /// the supplied delegate against <see cref="ExistingTargetIds"/> and returns entries
    /// whose targets do not exist.
    /// </summary>
    public void SetupCheckIntegrity()
    {
        MockIdMapStore
            .Setup(s => s.CheckIntegrityAsync(
                It.IsAny<Func<int, CancellationToken, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (Func<int, CancellationToken, Task<bool>> targetExistsAsync, CancellationToken ct) =>
            {
                var stale = new List<IdMapEntry>();
                foreach (var entry in ConfiguredMappings)
                {
                    var exists = await targetExistsAsync(entry.TargetId, ct).ConfigureAwait(false);
                    if (!exists)
                        stale.Add(entry);
                }
                return (IReadOnlyList<IdMapEntry>)stale;
            });
    }
}
