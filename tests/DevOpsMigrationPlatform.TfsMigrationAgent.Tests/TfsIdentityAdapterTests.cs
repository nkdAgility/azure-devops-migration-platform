// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
[TestCategory("UnitTests")]
[TestCategory("NET481")]
public sealed class TfsIdentityAdapterTests
{
    private static Mock<ILogger<TfsIdentityAdapter>> NewLogger() => new();

    private static void VerifyWarned(Mock<ILogger<TfsIdentityAdapter>> logger)
    {
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public async Task FindByUpnAsync_ReturnsEmpty_AndLogsWarning()
    {
        var logger = NewLogger();
        var adapter = new TfsIdentityAdapter(logger.Object);

        var result = await adapter.FindByUpnAsync("user@tfs.local", "proj", CancellationToken.None);

        Assert.AreEqual(0, result.Count);
        VerifyWarned(logger);
    }

    [TestMethod]
    public async Task FindByDisplayNameAsync_ReturnsEmpty_AndLogsWarning()
    {
        var logger = NewLogger();
        var adapter = new TfsIdentityAdapter(logger.Object);

        var result = await adapter.FindByDisplayNameAsync("Some User", "proj", CancellationToken.None);

        Assert.AreEqual(0, result.Count);
        VerifyWarned(logger);
    }

    [TestMethod]
    public void Constructor_NullLogger_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new TfsIdentityAdapter(null!));
    }
}
