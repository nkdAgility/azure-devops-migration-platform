// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Canonical port for enumerating exported work item revisions from a package
/// (ADR-0023 / VS-H1). Shared by the Prepare, Import-failure, Node-validation and
/// WorkItemType-validation slices; the single implementation lives in Infrastructure.Agent.
/// </summary>
public interface IWorkItemRevisionReader
{
    /// <summary>
    /// Enumerates every <c>revision.json</c> artefact in the WorkItems module of the package,
    /// yielding one <see cref="ParsedWorkItemRevision"/> per distinct revision path. Payloads
    /// that are missing, empty, or invalid JSON yield a result with a <c>ParseError</c> and a
    /// <see langword="null"/> revision instead of throwing.
    /// </summary>
    IAsyncEnumerable<ParsedWorkItemRevision> EnumerateAsync(
        IPackageAccess package,
        string organisation,
        string project,
        CancellationToken cancellationToken);
}

/// <summary>A single parsed work item revision artefact read from the package.</summary>
public sealed record ParsedWorkItemRevision(
    string RevisionJsonPath,
    string RevisionFolderPath,
    WorkItemRevision? Revision,
    string? ParseError);
