using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Reads and writes the per-job migration configuration file (<c>migration-config.json</c>)
/// that the CLI writes to the package root before job submission.
/// The agent reads this file to obtain the full <see cref="MigrationOptions"/> —
/// source, target, credentials, modules, policies, and tools — that is not carried
/// in the minimal <see cref="DevOpsMigrationPlatform.Abstractions.Jobs.Job"/>.
/// </summary>
/// <remarks>
/// <para><b>CLI usage (write path)</b>: Call <see cref="WriteAsync"/> after resolving
/// <c>outputPath</c> and before calling <c>ControlPlaneClient.SubmitAsync</c>.
/// If <see cref="WriteAsync"/> throws, the job MUST NOT be submitted (FR-007).</para>
/// <para><b>Agent usage (read path)</b>: Call <see cref="ReadAsync"/> after opening
/// the package store, before executing any module. If the file is absent,
/// <see cref="PackageConfigNotFoundException"/> is thrown and the job must fail fast.</para>
/// <para><b>Net481 compatibility</b>: All signatures use only types available in
/// .NET Framework 4.8.1. No default interface methods; no .NET 10-only types.</para>
/// </remarks>
public interface IPackageConfigStore
{
    /// <summary>
    /// Copies the raw scenario config file at <paramref name="sourceFilePath"/> verbatim
    /// to <c>migration-config.json</c> at the root of the package identified by
    /// <paramref name="packageUri"/>.
    /// No deserialization or re-serialization is performed — every byte is preserved exactly,
    /// including Tools, Generator, and any future sections not present in <c>MigrationOptions</c>.</summary>
    /// <remarks>
    /// Throws <see cref="System.InvalidOperationException"/> if the file already exists
    /// and <paramref name="force"/> is <c>false</c>
    /// (FR-012 — must not silently overwrite; the CLI must surface this as an error and
    /// require <c>--force</c> before overwriting).
    /// When <paramref name="force"/> is <c>true</c> the existing file is overwritten.
    /// Credential fields in the source file are written verbatim but
    /// MUST NOT appear in any log output (O-3 security constraint).
    /// </remarks>
    /// <param name="packageUri">
    /// The package URI (e.g. <c>file:///C:/output/my-package</c> or a local path).
    /// The implementation resolves the appropriate <see cref="IArtefactStore"/> internally.
    /// </param>
    /// <param name="sourceFilePath">Absolute path to the scenario config file to copy.</param>
    /// <param name="force">When <c>true</c>, overwrite an existing file instead of throwing.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task WriteAsync(
        string packageUri,
        string sourceFilePath,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <c>migration-config.json</c> from <paramref name="artefactStore"/> and
    /// returns an <see cref="IConfiguration"/> whose root is the content of that file,
    /// ready for <c>IOptions&lt;T&gt;</c> binding.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="IConfiguration"/> is built from the raw JSON bytes of the
    /// file via <c>ConfigurationBuilder.AddJsonStream()</c>. Callers bind sections via
    /// <c>packageConfig.GetSection("MigrationPlatform:Tools:FieldTransform")</c> etc.
    /// Throws <see cref="PackageConfigNotFoundException"/> if the file is absent (FR-005 —
    /// fail fast; no graceful fallback to defaults).
    /// Throws <see cref="System.Text.Json.JsonException"/> (or
    /// <c>Newtonsoft.Json.JsonException</c> on net481) if the file is present but
    /// cannot be parsed (FR-006).
    /// </remarks>
    /// <param name="artefactStore">The package store to read from.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>
    /// An <see cref="IConfiguration"/> rooted at the content of <c>migration-config.json</c>.
    /// </returns>
    Task<IConfiguration> ReadAsync(
        IArtefactStore artefactStore,
        CancellationToken cancellationToken = default);
}
