// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class AgentJobContextIntegrationTests
{
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

    [TestMethod]
    public void ActiveJobAgentJobContext_UsesExplicitCurrentContext_WhenAvailable()
    {
        var accessor = new CurrentAgentJobContextAccessor();
        accessor.Set(new AgentJobContext
        {
            Mode = "Inventory",
            PackagePath = @"C:\pkg",
            ConfigVersion = "2.0"
        });

        var context = new ActiveJobAgentJobContext(accessor);

        Assert.AreEqual("Inventory", context.Mode);
        Assert.AreEqual(@"C:\pkg", context.PackagePath);
        Assert.AreEqual("2.0", context.ConfigVersion);
    }

    [TestMethod]
    public void ActiveJobAgentJobContext_ReturnsEmptyValues_WhenNoCurrentContextExists()
    {
        var accessor = new CurrentAgentJobContextAccessor();
        var context = new ActiveJobAgentJobContext(accessor);

        Assert.AreEqual(string.Empty, context.Mode);
        Assert.AreEqual(string.Empty, context.PackagePath);
        Assert.AreEqual(string.Empty, context.ConfigVersion);
    }

    [TestMethod]
    public void ActiveJobSourceEndpointInfo_UsesExplicitCurrentSourceEndpoint_WhenAvailable()
    {
        var accessor = new CurrentJobEndpointAccessor();
        accessor.SetSource(new TestSourceEndpointInfo(
            "https://source.example",
            "SourceProject",
            "AzureDevOpsServices",
            new OrganisationEndpoint
            {
                ResolvedUrl = "https://source.example",
                Type = "AzureDevOpsServices"
            }));

        var endpoint = new ActiveJobSourceEndpointInfo(accessor);

        Assert.AreEqual("https://source.example", endpoint.Url);
        Assert.AreEqual("SourceProject", endpoint.Project);
        Assert.AreEqual("AzureDevOpsServices", endpoint.ConnectorType);
        Assert.AreEqual("https://source.example", endpoint.ToOrganisationEndpoint().ResolvedUrl);
    }

    [TestMethod]
    public void ActiveJobTargetEndpointInfo_UsesExplicitCurrentTargetEndpoint_WhenAvailable()
    {
        var accessor = new CurrentJobEndpointAccessor();
        accessor.SetTarget(new TestTargetEndpointInfo(
            "https://target.example",
            "TargetProject",
            "Simulated",
            new OrganisationEndpoint
            {
                ResolvedUrl = "https://target.example",
                Type = "Simulated"
            }));

        var endpoint = new ActiveJobTargetEndpointInfo(accessor);

        Assert.AreEqual("https://target.example", endpoint.Url);
        Assert.AreEqual("TargetProject", endpoint.Project);
        Assert.AreEqual("Simulated", endpoint.ConnectorType);
        Assert.AreEqual("https://target.example", endpoint.ToOrganisationEndpoint().ResolvedUrl);
    }

    [TestMethod]
    public void ActiveJobSourceEndpointInfo_ReturnsEmptyValues_WhenNoCurrentSourceExists()
    {
        var accessor = new CurrentJobEndpointAccessor();
        var endpoint = new ActiveJobSourceEndpointInfo(accessor);

        Assert.AreEqual(string.Empty, endpoint.Url);
        Assert.AreEqual(string.Empty, endpoint.Project);
        Assert.AreEqual(string.Empty, endpoint.ConnectorType);
        Assert.AreEqual(string.Empty, endpoint.ToOrganisationEndpoint().ResolvedUrl);
    }

    [TestMethod]
    public void ActiveJobTargetEndpointInfo_ReturnsEmptyValues_WhenNoCurrentTargetExists()
    {
        var accessor = new CurrentJobEndpointAccessor();
        var endpoint = new ActiveJobTargetEndpointInfo(accessor);

        Assert.AreEqual(string.Empty, endpoint.Url);
        Assert.AreEqual(string.Empty, endpoint.Project);
        Assert.AreEqual(string.Empty, endpoint.ConnectorType);
        Assert.AreEqual(string.Empty, endpoint.ToOrganisationEndpoint().ResolvedUrl);
    }

    private sealed record TestSourceEndpointInfo(string Url, string Project, string ConnectorType, OrganisationEndpoint Endpoint) : ISourceEndpointInfo
    {
        public OrganisationEndpoint ToOrganisationEndpoint() => Endpoint;
    }

    private sealed record TestTargetEndpointInfo(string Url, string Project, string ConnectorType, OrganisationEndpoint Endpoint) : ITargetEndpointInfo
    {
        public OrganisationEndpoint ToOrganisationEndpoint() => Endpoint;
    }
}
