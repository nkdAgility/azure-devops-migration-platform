// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using DevOpsMigrationPlatform.Abstractions.Storage;
using System.Text;
using System.IO;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class ImportCheckpointServiceTests
{
    [TestMethod]
    public async Task ReadCursorAsync_WhenCursorExists_ReturnsDeserializedCursor()
    {
        var expected = new CursorEntry
        {
            LastProcessed = "WorkItems/2026-01-01/638712000000000000-42-1",
            Stage = CursorStage.AppliedFields,
            UpdatedAt = new DateTimeOffset(2026, 01, 01, 10, 11, 12, TimeSpan.Zero)
        };

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Kind == PackageContentKind.Artefact &&
                    c.Address != null &&
                    c.Address.RelativePath == ".migration/Checkpoints/workitems-import.cursor.json"),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackagePayload?>(new PackagePayload(
                new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(expected))), "application/json")));

        var sut = new ImportCheckpointService(package.Object);

        var actual = await sut.ReadCursorAsync(CancellationToken.None);

        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.LastProcessed, actual.LastProcessed);
        Assert.AreEqual(expected.Stage, actual.Stage);
        Assert.AreEqual(expected.UpdatedAt, actual.UpdatedAt);
        package.VerifyAll();
    }

    [TestMethod]
    public async Task WriteCursorAsync_WritesSerializedCursorToExpectedKey()
    {
        var cursor = new CursorEntry
        {
            LastProcessed = "WorkItems/2026-01-01/638712000000000000-42-2",
            Stage = CursorStage.Completed,
            UpdatedAt = new DateTimeOffset(2026, 01, 01, 10, 11, 13, TimeSpan.Zero)
        };

        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package
            .Setup(p => p.PersistContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Kind == PackageContentKind.Artefact &&
                    c.Address != null &&
                    c.Address.RelativePath == ".migration/Checkpoints/workitems-import.cursor.json"),
                It.IsAny<PackagePayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
            {
                using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var json = reader.ReadToEnd();
                var persisted = JsonSerializer.Deserialize<CursorEntry>(json);
                Assert.IsNotNull(persisted);
                Assert.AreEqual(cursor.LastProcessed, persisted.LastProcessed);
                Assert.AreEqual(cursor.Stage, persisted.Stage);
            })
            .Returns(ValueTask.CompletedTask);

        var sut = new ImportCheckpointService(package.Object);

        await sut.WriteCursorAsync(cursor, CancellationToken.None);

        package.VerifyAll();
    }
}
