// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform;

[TestClass]
public class FieldTransformToolTests
{
    private static FieldTransformContext MakeContext()
        => new FieldTransformContext(1, 0, "Bug", FieldTransformPhase.Import);

    private static IReadOnlyDictionary<string, object?> EmptyFields()
        => new Dictionary<string, object?>();

    /// <summary>
    /// Creates a mock factory that returns a strict mock transform for any Create call.
    /// The returned transform's Apply returns fields unchanged with no actions.
    /// </summary>
    private static (Mock<IFieldTransformFactory> Factory, Mock<IFieldTransform> Transform) StrictMockFactory()
    {
        var transform = new Mock<IFieldTransform>(MockBehavior.Strict);
        transform.SetupGet(t => t.Type).Returns("Mock");
        transform.SetupGet(t => t.Name).Returns("mock");
        transform.Setup(t => t.Apply(
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<FieldTransformContext>()))
            .Returns((IReadOnlyDictionary<string, object?> f, FieldTransformContext _) =>
                new FieldTransformResult(f, System.Array.Empty<FieldTransformAction>()));

        var factory = new Mock<IFieldTransformFactory>(MockBehavior.Strict);
        factory.Setup(f => f.Create(
                It.IsAny<FieldTransformRuleOptions>(),
                It.IsAny<string>(),
                It.IsAny<int>()))
            .Returns(transform.Object);

        return (factory, transform);
    }

    private static FieldTransformOptions OptionsWithOneEnabledTransform()
        => new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[] { new FieldTransformRuleOptions { Type = "Mock", Enabled = true } }
                }
            }
        };

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabledForPhase_WhenEnabled_ReturnsTrue()
    {
        var (factory, _) = StrictMockFactory();
        var sut = new FieldTransformTool(
            Options.Create(OptionsWithOneEnabledTransform()),
            factory.Object,
            NullLoggerFactory.Instance);

        Assert.IsTrue(sut.IsEnabledForPhase(FieldTransformPhase.Import));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabledForPhase_WhenDisabled_ReturnsFalse()
    {
        var options = new FieldTransformOptions
        {
            Enabled = false,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[] { new FieldTransformRuleOptions { Type = "Mock", Enabled = true } }
                }
            }
        };

        // Factory is not called because the constructor builds the pipeline regardless of Enabled,
        // but IsEnabledForPhase checks _options.Enabled first.
        var (factory, _) = StrictMockFactory();
        var sut = new FieldTransformTool(
            Options.Create(options),
            factory.Object,
            NullLoggerFactory.Instance);

        Assert.IsFalse(sut.IsEnabledForPhase(FieldTransformPhase.Import));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabledForPhase_WhenNoTransforms_ReturnsFalse()
    {
        var options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new List<FieldTransformGroupOptions>()
        };

        // No transforms → factory.Create never called
        var factory = new Mock<IFieldTransformFactory>(MockBehavior.Strict);

        var sut = new FieldTransformTool(
            Options.Create(options),
            factory.Object,
            NullLoggerFactory.Instance);

        Assert.IsFalse(sut.IsEnabledForPhase(FieldTransformPhase.Import));
        factory.VerifyNoOtherCalls();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ApplyTransforms_IsStatelessAcrossInvocations()
    {
        var (factory, transform) = StrictMockFactory();
        var sut = new FieldTransformTool(
            Options.Create(OptionsWithOneEnabledTransform()),
            factory.Object,
            NullLoggerFactory.Instance);

        var fields1 = new Dictionary<string, object?> { ["System.Title"] = "First Call" };
        var fields2 = new Dictionary<string, object?> { ["System.Title"] = "Second Call" };

        var result1 = sut.ApplyTransforms(fields1, MakeContext());
        var result2 = sut.ApplyTransforms(fields2, MakeContext());

        // Each call should process the fields passed in, not leak state from previous call
        Assert.AreEqual("First Call", result1.Fields["System.Title"]);
        Assert.AreEqual("Second Call", result2.Fields["System.Title"]);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Constructor_WhenMoreThan100Transforms_LogsWarning()
    {
        // Arrange: build 101 transform rules across one group
        var rules = new List<FieldTransformRuleOptions>();
        for (int i = 0; i < 101; i++)
            rules.Add(new FieldTransformRuleOptions { Type = "Mock", Enabled = true });

        var options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions { Enabled = true, Transforms = rules }
            }
        };

        var (factory, _) = StrictMockFactory();

        var mockLogger = new Mock<ILogger<FieldTransformTool>>();
        var mockLoggerFactory = new Mock<ILoggerFactory>();
        mockLoggerFactory
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns((string name) =>
            {
                if (name == typeof(FieldTransformTool).FullName)
                    return mockLogger.Object;
                return NullLogger.Instance;
            });

        // Act
        _ = new FieldTransformTool(Options.Create(options), factory.Object, mockLoggerFactory.Object);

        // Assert: warning was logged
        mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<System.Exception?>(),
                It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()),
            Times.Once);
    }
}
