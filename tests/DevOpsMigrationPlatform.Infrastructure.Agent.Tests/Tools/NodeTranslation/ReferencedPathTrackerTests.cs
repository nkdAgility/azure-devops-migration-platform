// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

[TestClass]
public class ReferencedPathTrackerTests
{
    private static Mock<IArtefactStore> CreateStoreMock(string? existingJson = null)
    {
        var mock = new Mock<IArtefactStore>(MockBehavior.Loose);
        mock.Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingJson);
        mock.Setup(s => s.WriteAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static ReferencedPathTracker CreateTracker()
        => new ReferencedPathTracker(NullLogger<ReferencedPathTracker>.Instance);

    [TestMethod]
    public async Task RecordAreaPathAsync_NewPath_WritesArtifact()
    {
        var storeMock = CreateStoreMock();
        var tracker = CreateTracker();

        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", storeMock.Object, CancellationToken.None);

        storeMock.Verify(s => s.WriteAsync(
            "Nodes/referenced-paths.json",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(1, tracker.AreaPaths.Count);
        Assert.IsTrue(tracker.AreaPaths.Contains(@"ProjectA\Team A"));
    }

    [TestMethod]
    public async Task RecordAreaPathAsync_DuplicatePath_DoesNotWriteAgain()
    {
        var storeMock = CreateStoreMock();
        var tracker = CreateTracker();

        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", storeMock.Object, CancellationToken.None);
        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", storeMock.Object, CancellationToken.None);

        storeMock.Verify(s => s.WriteAsync(
            "Nodes/referenced-paths.json",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(1, tracker.AreaPaths.Count);
    }

    [TestMethod]
    public async Task RecordAreaPathAsync_CaseInsensitiveDuplicate_DoesNotWriteAgain()
    {
        var storeMock = CreateStoreMock();
        var tracker = CreateTracker();

        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", storeMock.Object, CancellationToken.None);
        await tracker.RecordAreaPathAsync(@"PROJECTA\TEAM A", storeMock.Object, CancellationToken.None);

        storeMock.Verify(s => s.WriteAsync(
            "Nodes/referenced-paths.json",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(1, tracker.AreaPaths.Count);
    }

    [TestMethod]
    public async Task InitializeAsync_LoadsExistingPaths()
    {
        var artifact = new ReferencedPathsArtifact(
            new List<string> { @"ProjectA\Team A" },
            new List<string> { @"ProjectA\Sprint 1" });
        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var storeMock = CreateStoreMock(json);
        var tracker = CreateTracker();

        await tracker.InitializeAsync(storeMock.Object, CancellationToken.None);

        Assert.AreEqual(1, tracker.AreaPaths.Count);
        Assert.AreEqual(1, tracker.IterationPaths.Count);
        Assert.IsTrue(tracker.AreaPaths.Contains(@"ProjectA\Team A"));
    }

    [TestMethod]
    public async Task InitializeAsync_ThenRecordExisting_DoesNotWriteAgain()
    {
        var artifact = new ReferencedPathsArtifact(
            new List<string> { @"ProjectA\Team A" },
            new List<string>());
        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var storeMock = CreateStoreMock(json);
        var tracker = CreateTracker();

        await tracker.InitializeAsync(storeMock.Object, CancellationToken.None);
        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", storeMock.Object, CancellationToken.None);

        storeMock.Verify(s => s.WriteAsync(
            "Nodes/referenced-paths.json",
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
