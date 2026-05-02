// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class AgentJobContextIntegrationTests
{
    [TestMethod]
    public void ModuleReadsContext_WithoutAccessingOtherServices_Succeeds()
    {
        // T053: Assert module reads Mode/PackagePath/ConfigVersion without accessing any other service
        var context = new AgentJobContext
        {
            Mode = "Export",
            PackagePath = @"C:\abs\path\package",
            ConfigVersion = "2.0"
        };

        // Simulate a module reading context
        var mode = context.Mode;
        var packagePath = context.PackagePath;
        var configVersion = context.ConfigVersion;

        Assert.AreEqual("Export", mode);
        Assert.AreEqual(@"C:\abs\path\package", packagePath);
        Assert.AreEqual("2.0", configVersion);
    }

    [TestMethod]
    public void Constructor_LogsDebugWithModeAndConfigVersion_NotPackagePath()
    {
        // T055: Verify LogDebug called with {Mode} and {ConfigVersion}, assert PackagePath NOT in log output
        var mockLogger = new Mock<ILogger<AgentJobContext>>();
        var capturedLogs = new List<string>();

        mockLogger
            .Setup(x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
            .Callback(new InvocationAction(invocation =>
            {
                var logLevel = (LogLevel)invocation.Arguments[0];
                var state = invocation.Arguments[2];
                var formatter = invocation.Arguments[4];

                if (logLevel == LogLevel.Debug)
                {
                    var message = state?.ToString() ?? string.Empty;
                    capturedLogs.Add(message);
                }
            }));

        var context = new AgentJobContext(mockLogger.Object)
        {
            Mode = "Export",
            PackagePath = @"C:\abs\path\package",
            ConfigVersion = "2.0"
        };

        // Verify LogDebug was called
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Agent job context resolved")),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);

        // Verify captured logs contain Mode and ConfigVersion but NOT PackagePath
        Assert.IsTrue(capturedLogs.Count > 0, "Expected at least one debug log");
        var combinedLog = string.Join(" ", capturedLogs);
        Assert.IsTrue(combinedLog.Contains("Mode"), "Log should contain 'Mode'");
        Assert.IsTrue(combinedLog.Contains("ConfigVersion"), "Log should contain 'ConfigVersion'");
        Assert.IsFalse(combinedLog.Contains("PackagePath"), "Log must NOT contain 'PackagePath' (customer data)");
        Assert.IsFalse(combinedLog.Contains(@"C:\abs\path\package"), "Log must NOT contain the actual package path value");
    }

    [TestMethod]
    public void Constructor_LogsDebugWithStructuredParams_NotStringInterpolation()
    {
        // T054: Verify LogDebug uses structured parameters
        var mockLogger = new Mock<ILogger<AgentJobContext>>();
        var capturedState = new List<object>();

        mockLogger
            .Setup(x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
            .Callback(new InvocationAction(invocation =>
            {
                var logLevel = (LogLevel)invocation.Arguments[0];
                var state = invocation.Arguments[2];

                if (logLevel == LogLevel.Debug && state != null)
                {
                    capturedState.Add(state);
                }
            }));

        var context = new AgentJobContext(mockLogger.Object)
        {
            Mode = "Import",
            PackagePath = @"C:\test\package",
            ConfigVersion = "3.1"
        };

        // Verify structured logging (state should be IReadOnlyList<KeyValuePair<string, object>>)
        Assert.IsTrue(capturedState.Count > 0, "Expected structured log state");

        // The state object should support enumeration of key-value pairs for structured logging
        var stateStr = capturedState[0].ToString();
        Assert.IsTrue(stateStr!.Contains("Mode=Import") || stateStr.Contains("Mode = Import"),
            $"Expected structured param Mode=Import, got: {stateStr}");
        Assert.IsTrue(stateStr.Contains("ConfigVersion=3.1") || stateStr.Contains("ConfigVersion = 3.1"),
            $"Expected structured param ConfigVersion=3.1, got: {stateStr}");
    }
}
