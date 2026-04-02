using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Contract for a data type module. Modules are the only extension point for
/// adding new data types to the migration platform.
/// See docs/modules.md for the full module architecture.
/// </summary>
public interface IDataTypeModule
{
    /// <summary>Unique module name, e.g. "WorkItems". Must be unique across all registered modules.</summary>
    string Name { get; }

    /// <summary>
    /// Modules this one depends on. The orchestrator performs a topological sort
    /// before execution; circular dependencies are a fatal configuration error.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<string> DependsOn { get; }

    /// <summary>Export data from the source system into the package via IArtefactStore.</summary>
    Task ExportAsync(ExportContext context, CancellationToken ct);

    /// <summary>Import data from the package into the target system via IArtefactStore.</summary>
    Task ImportAsync(ImportContext context, CancellationToken ct);

    /// <summary>Validate the package or target without side effects.</summary>
    Task ValidateAsync(ValidationContext context, CancellationToken ct);
}
