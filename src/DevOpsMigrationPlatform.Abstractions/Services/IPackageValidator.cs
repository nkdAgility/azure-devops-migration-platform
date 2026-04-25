using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Validation;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Validates the migration package before or after a migration run.
/// Must be side-effect free — no package files are modified or created.
/// </summary>
public interface IPackageValidator
{
    Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken);
}
