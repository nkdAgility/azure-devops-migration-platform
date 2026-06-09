// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class AgentJobContextTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_InvalidMode_ThrowsInvalidOperationException()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            _ = new AgentJobContext
            {
                Mode = "InvalidMode",
                PackagePath = @"C:\temp\package",
                ConfigVersion = "2.0"
            };
        });

        Assert.IsTrue(ex.Message.Contains("Invalid Mode"));
        Assert.IsTrue(ex.Message.Contains("InvalidMode"));
        Assert.IsTrue(ex.Message.Contains("Inventory"));
        Assert.IsTrue(ex.Message.Contains("Dependencies"));
        Assert.IsTrue(ex.Message.Contains("Export"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_InventoryMode_Succeeds()
    {
        var context = new AgentJobContext
        {
            Mode = "Inventory",
            PackagePath = @"C:\temp\package",
            ConfigVersion = "2.0"
        };

        Assert.AreEqual("Inventory", context.Mode);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_DependenciesMode_Succeeds()
    {
        var context = new AgentJobContext
        {
            Mode = "Dependencies",
            PackagePath = @"C:\temp\package",
            ConfigVersion = "2.0"
        };

        Assert.AreEqual("Dependencies", context.Mode);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_RelativePackagePath_ThrowsInvalidOperationException()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            _ = new AgentJobContext
            {
                Mode = "Export",
                PackagePath = "relative\\path",
                ConfigVersion = "2.0"
            };
        });

        Assert.IsTrue(ex.Message.Contains("PackagePath must be an absolute path"));
        Assert.IsTrue(ex.Message.Contains("relative\\path"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_EmptyPackagePath_ThrowsInvalidOperationException()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            _ = new AgentJobContext
            {
                Mode = "Export",
                PackagePath = string.Empty,
                ConfigVersion = "2.0"
            };
        });

        Assert.IsTrue(ex.Message.Contains("PackagePath must be an absolute path"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_UnixAbsolutePath_Succeeds()
    {
        var context = new AgentJobContext
        {
            Mode = "Export",
            PackagePath = "/tmp/package",
            ConfigVersion = "2.0"
        };

        Assert.AreEqual("/tmp/package", context.PackagePath);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_UNCPath_Succeeds()
    {
        var context = new AgentJobContext
        {
            Mode = "Export",
            PackagePath = @"\\server\share\package",
            ConfigVersion = "2.0"
        };

        Assert.AreEqual(@"\\server\share\package", context.PackagePath);
    }

    // T055: LogDebug called with Mode and ConfigVersion
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_LogsDebug_WithModeAndConfigVersion_WhenBothSet()
    {
        var logMessages = new System.Collections.Generic.List<(LogLevel Level, string Message)>();
        var logger = new CapturingLogger(logMessages);

        _ = new AgentJobContext(logger)
        {
            Mode = "Import",
            PackagePath = @"C:\temp\package",
            ConfigVersion = "3.0"
        };

        Assert.IsTrue(logMessages.Count > 0, "Expected at least one LogDebug call");
        var debugMsg = logMessages.Find(m => m.Level == LogLevel.Debug);
        Assert.IsNotNull(debugMsg.Message, "Expected a Debug-level log entry");
        Assert.IsTrue(debugMsg.Message.Contains("Import"), "Expected Mode in log");
        Assert.IsTrue(debugMsg.Message.Contains("3.0"), "Expected ConfigVersion in log");
        // PackagePath must NOT appear in any log (DataClassification.Customer)
        foreach (var (_, msg) in logMessages)
        {
            Assert.IsFalse(msg.Contains(@"C:\temp\package"), "PackagePath must not appear in logs");
        }
    }

    // S3: ContextIsReadOnly_ModuleAccesses_NoWritePath
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IAgentJobContext_Interface_HasOnlyReadOnlyProperties()
    {
        var props = typeof(IAgentJobContext).GetProperties();

        Assert.IsTrue(props.Length > 0, "IAgentJobContext must expose at least one property");
        foreach (var prop in props)
        {
            Assert.IsTrue(prop.CanRead, $"Property '{prop.Name}' must be readable");
            var setter = prop.GetSetMethod(nonPublic: false);
            Assert.IsNull(setter, $"Property '{prop.Name}' must not have a public setter");
        }
    }

    // S4: TfsSourceOnlyJob_ContextResolved_NoTargetInfo
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AgentJobContext_ContextResolvesWithoutTargetEndpointDependency()
    {
        var accessor = new CurrentAgentJobContextAccessor();
        accessor.Set(new AgentJobContext
        {
            Mode = "Export",
            PackagePath = @"C:\exports\run-001",
            ConfigVersion = "2.0"
        });

        var context = new ActiveJobAgentJobContext(accessor);

        Assert.AreEqual("Export", context.Mode);
        Assert.AreEqual(@"C:\exports\run-001", context.PackagePath);
        // ITargetEndpointInfo is not accessed — this compiles and runs without it
    }

    private sealed class CapturingLogger(System.Collections.Generic.List<(LogLevel, string)> captured)
        : ILogger<AgentJobContext>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => captured.Add((logLevel, formatter(state, exception)));
    }
}
