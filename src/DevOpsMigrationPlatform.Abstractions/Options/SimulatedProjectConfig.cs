using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Configuration for one simulated project, including the work item types to generate
/// and structural characteristics (links, attachments, comments).
/// </summary>
public sealed class SimulatedProjectConfig
{
    /// <summary>Project name. Must be non-empty.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Work item types and their generation parameters.</summary>
    public List<SimulatedWorkItemTypeConfig> WorkItemTypes { get; set; } = new();

    /// <summary>
    /// Link topology: <c>"Flat"</c> (default), <c>"Tree"</c>, or <c>"TreeWithCrossLinks"</c>.
    /// Controls whether parent-child links are generated between work items.
    /// </summary>
    public string LinkTopology { get; set; } = "Flat";

    /// <summary>
    /// Size of each attachment in kilobytes. 0 means no attachments are generated.
    /// </summary>
    public int AttachmentSizeKb { get; set; }

    /// <summary>When <c>true</c>, synthetic comments are generated per work item revision.</summary>
    public bool HasComments { get; set; }

    /// <summary>When <c>true</c>, description fields contain embedded image references.</summary>
    public bool HasEmbeddedImages { get; set; }
}
