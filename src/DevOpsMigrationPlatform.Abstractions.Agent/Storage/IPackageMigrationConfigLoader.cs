// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Reads the per-job migration configuration file (<c>migration-config.json</c>)
/// from the package boundary.
/// The agent reads this file to obtain the full <see cref="MigrationPlatformOptions"/> —
/// source, target, credentials, modules, policies, and tools — that is not carried
/// in the minimal <see cref="DevOpsMigrationPlatform.Abstractions.Jobs.Job"/>.
/// </summary>
/// <remarks>
/// <para><b>Agent usage (read path)</b>: Call <see cref="LoadAsync"/> after opening
/// the package boundary, before executing any module. If the file is absent,
/// <see cref="PackageConfigNotFoundException"/> is thrown and the job must fail fast.</para>
/// <para><b>Net481 compatibility</b>: All signatures use only types available in
/// .NET Framework 4.8.1. No default interface methods; no .NET 10-only types.</para>
/// </remarks>
public interface IPackageMigrationConfigLoader
{
    /// <summary>
    /// Reads <c>migration-config.json</c> from the package boundary and
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
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>
    /// An <see cref="IConfiguration"/> rooted at the content of <c>migration-config.json</c>.
    /// </returns>
    Task<IConfiguration> LoadAsync(CancellationToken cancellationToken = default);
}
