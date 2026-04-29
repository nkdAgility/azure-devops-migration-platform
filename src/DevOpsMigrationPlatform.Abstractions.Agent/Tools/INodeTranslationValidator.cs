using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Validates NodeTranslation configuration against the package contents.
/// No side effects — read-only scan.
/// </summary>
public interface INodeTranslationValidator
{
    /// <summary>
    /// Scans the package for path coverage. Uses <c>Nodes/referenced-paths.json</c> if available,
    /// otherwise falls back to scanning all <c>revision.json</c> files.
    /// </summary>
    Task<NodeTranslationValidationReport> ValidateAsync(
        IArtefactStore artefactStore,
        ProjectMapping context,
        CancellationToken ct);
}
