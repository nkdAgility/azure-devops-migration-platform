using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

/// <summary>
/// Shared scenario state and mocks for WatermarkStore step definitions.
/// </summary>
public class WatermarkStoreContext
{
    /// <summary>The watermark store under test (file-based via IStateStore).</summary>
    public IWorkItemWatermarkStore? Sut { get; set; }

    /// <summary>Captured watermark for a given work item ID after a write.</summary>
    public Dictionary<int, int?> Watermarks { get; } = new();

    /// <summary>Captured query counts by query string.</summary>
    public Dictionary<string, int?> QueryCounts { get; } = new();

    /// <summary>Set to true after a simulated restart replaces Sut with a fresh instance over the same store.</summary>
    public bool Restarted { get; set; }

    /// <summary>Temp directory used by FileSystemStateStore so the store can be re-opened after a simulated restart.</summary>
    public string? StoreDirectory { get; set; }
}
