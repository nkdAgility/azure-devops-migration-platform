// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent;

/// <summary>
/// Cross-cutting contract for a module extension: one composable unit of a module's capability.
/// </summary>
/// <remarks>
/// <para>
/// The extension pattern: a module's behaviour is composed from independent
/// <see cref="IModuleExtension"/> implementations. The module resolves the registered set from DI,
/// filters by <see cref="IsEnabled"/> and the relevant <see cref="SupportsExport"/> /
/// <see cref="SupportsImport"/> flag, sorts by <see cref="Order"/>, and hands the resulting list to
/// its orchestrator, which runs each extension per entity. The module never knows what any extension
/// does or how it is configured.
/// </para>
/// <para>
/// Each extension owns its own configuration: an optional extension exposes its own
/// <c>IOptions&lt;T&gt;</c> whose options carry an <c>Enabled</c> flag (plus any extension-specific
/// settings) and returns <see cref="IsEnabled"/> from it; a mandatory extension has no <c>Enabled</c>
/// option and returns <see langword="true"/>. No shared, module-level options object enumerates the
/// extensions — adding an extension never requires editing a central config class.
/// </para>
/// <para>
/// Export and import are capabilities of a single extension (declared via <see cref="SupportsExport"/>
/// and <see cref="SupportsImport"/>) — never split into export-only / import-only types. Module-specific
/// extension contracts extend this interface to add the phase methods over a module-specific context.
/// </para>
/// </remarks>
public interface IModuleExtension
{
    /// <summary>Name of the owning module (e.g. "Teams", "WorkItems").</summary>
    string Module { get; }

    /// <summary>Unique name of this extension within its module (e.g. "BoardConfig").</summary>
    string Name { get; }

    /// <summary>Execution order within the module. Lower runs earlier.</summary>
    int Order { get; }

    /// <summary>True when this extension participates in the export phase.</summary>
    bool SupportsExport { get; }

    /// <summary>True when this extension participates in the import phase.</summary>
    bool SupportsImport { get; }

    /// <summary>
    /// Whether this extension is active. The extension answers from its own configuration
    /// (its own <c>IOptions&lt;T&gt;</c>): a mandatory extension returns <see langword="true"/>;
    /// an optional extension returns its <c>Enabled</c> setting. It does not depend on any shared
    /// module-level options object.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Exports extension-specific data for a single entity into the package.
    /// Called by the orchestrator once per entity for every enabled extension where
    /// <see cref="SupportsExport"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="context">
    /// Per-entity context. Cast to the domain-specific context type to access entity data.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task ExportAsync(IExtensionContext context, CancellationToken ct);

    /// <summary>
    /// Imports extension-specific data from the package into the target system.
    /// Called by the orchestrator once per entity for every enabled extension where
    /// <see cref="SupportsImport"/> is <see langword="true"/>.
    /// <see cref="IExtensionContext.TargetEntityId"/> is set before this call.
    /// </summary>
    /// <param name="context">
    /// Per-entity context. Cast to the domain-specific context type to access entity and
    /// target data. <see cref="IExtensionContext.TargetEntityId"/> is guaranteed non-null.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task ImportAsync(IExtensionContext context, CancellationToken ct);
}
