using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Contract for a migration module. Modules are the only extension point for
/// adding new capabilities to the migration platform.
/// See docs/modules.md for the full module architecture.
/// </summary>
public interface IModule
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
