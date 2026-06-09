// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System.IO;
using System.Text;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Checkpointing;

[TestClass]
public sealed class PhaseTrackingServiceTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task WritePhaseRecordAsync_WithPackageBoundary_PersistsViaMetaRouting()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        package.Setup(p => p.PersistMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.PhaseRecord),
                It.IsAny<PackageMetaPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var sut = new PhaseTrackingService(package.Object);

        await sut.WritePhaseRecordAsync(new JobPhaseRecord { ExportCompleted = true }, CancellationToken.None);

        package.Verify(p => p.PersistMetaAsync(
            It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.PhaseRecord),
            It.IsAny<PackageMetaPayload>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ReadPhaseRecordAsync_WithPackageBoundary_ReadsViaMetaRouting()
    {
        var package = new Mock<IPackageAccess>(MockBehavior.Strict);
        var payload = "{\"ExportCompleted\":true,\"PrepareCompleted\":false,\"ImportCompleted\":false}";
        package.Setup(p => p.RequestMetaAsync(
                It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.PhaseRecord),
                It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<PackageMetaResult>(new PackageMetaResult(".migration/phase.json", new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes(payload))))));

        var sut = new PhaseTrackingService(package.Object);

        var record = await sut.ReadPhaseRecordAsync(CancellationToken.None);

        Assert.IsTrue(record.ExportCompleted);
        package.Verify(p => p.RequestMetaAsync(
            It.Is<PackageMetaContext>(c => c.Kind == PackageMetaKind.PhaseRecord),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
