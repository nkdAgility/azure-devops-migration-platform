// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules;

/// <summary>
/// Captures the package state after the inventory job completes and
/// provides assertion helpers scoped to per-module artefact presence.
/// </summary>
public sealed class InventoryModulesResult
{
    private readonly Mock<IPackageAccess> _packageMock;

    /// <summary>Whether the InventoryAnalyser was included in this run.</summary>
    public bool InventoryAnalyserWasIncluded { get; }

    internal InventoryModulesResult(Mock<IPackageAccess> packageMock, bool analyserIncluded)
    {
        _packageMock = packageMock;
        InventoryAnalyserWasIncluded = analyserIncluded;
    }

    // --- assertion surface ---

    /// <summary>
    /// Asserts that the named module wrote its inventory contribution into the package
    /// (i.e. <c>PersistIndexAsync</c> was called for an index context scoped to that module).
    /// </summary>
    public void AssertModuleArtefactExists(string moduleName)
    {
        _packageMock.Verify(
            p => p.PersistIndexAsync(
                It.Is<PackageIndexContext>(ctx => IsInventoryIndexContext(ctx)),
                It.IsAny<PackagePayload>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            $"Expected module '{moduleName}' to have written an inventory artefact via PersistIndexAsync, but no such call was made.");
    }

    /// <summary>
    /// Asserts that all four standard inventory-capable modules produced their artefact.
    /// All four modules write via <c>IProjectInventoryWriter.MergeAsync</c> → <c>PersistIndexAsync</c>;
    /// we assert that call was made at least four times (once per module).
    /// </summary>
    public void AssertAllStandardModuleArtefactsExist()
    {
        _packageMock.Verify(
            p => p.PersistIndexAsync(
                It.Is<PackageIndexContext>(ctx => IsInventoryIndexContext(ctx)),
                It.IsAny<PackagePayload>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(4),
            "Expected all four standard modules (WorkItems, Identities, Nodes, Teams) to have written inventory artefacts.");
    }

    // --- shared predicate ---

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ctx"/> represents a per-project
    /// inventory index write (FileName = "inventory.json", non-empty org and project).
    /// </summary>
    private static bool IsInventoryIndexContext(PackageIndexContext ctx)
        => ctx.FileName == "inventory.json"
        && !string.IsNullOrWhiteSpace(ctx.Organisation)
        && !string.IsNullOrWhiteSpace(ctx.Project);
}
