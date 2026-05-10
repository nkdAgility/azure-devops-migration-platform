// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class MultiRunAuthorityIsolationTests
{
    [TestMethod]
    public async Task ActionQualifiedCursors_DoNotCollideAcrossExportAndImport()
    {
        var store = new MemoryStateStore();
        var source = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        source.SetupGet(x => x.Url).Returns("https://dev.azure.com/contoso");
        source.SetupGet(x => x.Project).Returns("Shop");
        source.SetupGet(x => x.ConnectorType).Returns("AzureDevOpsServices");
        var endpoints = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        endpoints.SetupGet(x => x.Source).Returns(source.Object);
        endpoints.SetupGet(x => x.Target).Returns((ITargetEndpointInfo?)null);

        var sut = new CheckpointingService(
            store,
            endpoints.Object,
            package: PackageTestFactory.CreateStateDelegatingMock(store).Object);
        await sut.WriteCursorAsync("export.workitems", new CursorEntry { LastProcessed = "E", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        await sut.WriteCursorAsync("import.workitems", new CursorEntry { LastProcessed = "I", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);

        var export = await sut.ReadCursorAsync("export.workitems", CancellationToken.None);
        var import = await sut.ReadCursorAsync("import.workitems", CancellationToken.None);
        Assert.AreEqual("E", export?.LastProcessed);
        Assert.AreEqual("I", import?.LastProcessed);
    }

    private sealed class MemoryStateStore : IStateStore
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);
        public Task<string?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(_store.TryGetValue(key, out var value) ? value : null);
        public Task WriteAsync(string key, string content, CancellationToken ct) { _store[key] = content; return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(_store.ContainsKey(key));
        public Task DeleteAsync(string key, CancellationToken ct) { _store.Remove(key); return Task.CompletedTask; }
    }
}
