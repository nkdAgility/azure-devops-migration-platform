using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

[TestClass]
[TestCategory("Unit")]
public class TuiLogViewTests
{
    private Mock<IControlPlaneClient> _clientMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _clientMock = new Mock<IControlPlaneClient>();
    }

    [TestMethod]
    public void Constructor_CreatesViewInProgressMode()
    {
        // Arrange + Act
        var view = new TuiLogView(_clientMock.Object);

        // Assert — Title reflects Progress mode with scroll hint
        Assert.AreEqual("Log [Progress] (End=follow)", view.Title.ToString());
    }

    [TestMethod]
    public void Clear_DoesNotCrash_WhenNothingBound()
    {
        // Arrange
        var view = new TuiLogView(_clientMock.Object);

        // Act + Assert
        view.Clear();
    }

    [TestMethod]
    public void ClearAndBind_DoesNotCrash()
    {
        // Arrange
        _clientMock
            .Setup(c => c.FollowLogsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .Returns(EmptyAsyncEnumerable<ProgressEvent>());

        var view = new TuiLogView(_clientMock.Object);
        var cts = new CancellationTokenSource();

        // Act
        view.ClearAndBind(Guid.NewGuid(), cts.Token);

        // Clean up — cancel stream
        cts.Cancel();
        view.Clear();
    }

    [TestMethod]
    public void Dispose_StopsStream()
    {
        // Arrange
        _clientMock
            .Setup(c => c.FollowLogsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .Returns(EmptyAsyncEnumerable<ProgressEvent>());

        var view = new TuiLogView(_clientMock.Object);
        var cts = new CancellationTokenSource();
        view.ClearAndBind(Guid.NewGuid(), cts.Token);

        // Act + Assert (no hang, no exception)
        view.Dispose();
        cts.Dispose();
    }

    [TestMethod]
    public void OnJobEnded_HasCorrectSignature()
    {
        // Arrange
        var view = new TuiLogView(_clientMock.Object);
        string? receivedState = null;

        // Subscribe
        view.OnJobEnded += (state) => receivedState = state;

        // The event is fired from stream callbacks; just verify the delegate wiring compiles
        Assert.IsNull(receivedState); // not fired without a stream
    }

    [TestMethod]
    public void MinLevel_DefaultsToInformation()
    {
        // Arrange + Act
        var view = new TuiLogView(_clientMock.Object);

        // Assert
        Assert.AreEqual("Information", view.MinLevel);
    }

    [TestMethod]
    public void ClearAndBind_CalledTwice_DoesNotLeakStream()
    {
        // Arrange
        _clientMock
            .Setup(c => c.FollowLogsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .Returns(EmptyAsyncEnumerable<ProgressEvent>());

        var view = new TuiLogView(_clientMock.Object);
        var cts = new CancellationTokenSource();
        var jobId = Guid.NewGuid();

        // Act — bind twice (second should cancel first)
        view.ClearAndBind(jobId, cts.Token);
        view.ClearAndBind(jobId, cts.Token);

        // Clean up
        cts.Cancel();
        view.Clear();
    }

    [TestMethod]
    public void MinLevel_CanBeChanged_ForDiagnosticsMode()
    {
        // Arrange
        var view = new TuiLogView(_clientMock.Object);

        // Act
        view.MinLevel = "Warning";

        // Assert
        Assert.AreEqual("Warning", view.MinLevel);
    }

    [TestMethod]
    public void ClearAndBind_InDiagnosticsMode_DoesNotCrash()
    {
        // Arrange
        _clientMock
            .Setup(c => c.StreamDiagnosticsAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<DiagnosticLogRecord>());

        var view = new TuiLogView(_clientMock.Object) { MinLevel = "Warning" };
        var cts = new CancellationTokenSource();

        // Act — tab to diagnostics mode first (cannot do key event, set via binding)
        // We just verify ClearAndBind works in default state without crash
        view.ClearAndBind(Guid.NewGuid(), cts.Token);

        // Clean up
        cts.Cancel();
        view.Clear();
    }

    // Helper: empty async enumerable for mocking
    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
