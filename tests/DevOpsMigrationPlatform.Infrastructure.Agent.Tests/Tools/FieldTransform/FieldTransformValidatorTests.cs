using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform;

[TestClass]
public class FieldTransformValidatorTests
{
    private static FieldTransformValidator BuildValidator(
        FieldTransformOptions options,
        IFieldDefinitionProviderFactory? factory = null)
        => new FieldTransformValidator(
            Options.Create(options),
            NullLogger<FieldTransformValidator>.Instance,
            factory);

    [TestMethod]
    public async Task ValidateAsync_WhenToolDisabled_ReturnsValidReport()
    {
        var options = new FieldTransformOptions
        {
            Enabled = false,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[] { new FieldTransformRuleOptions { Type = string.Empty, Enabled = true } }
                }
            }
        };

        var sut = BuildValidator(options);
        var report = await sut.ValidateAsync(sampleSize: 1, cancellationToken: CancellationToken.None);

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.Entries.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_WithNoProviderFactory_ReturnsValidReport()
    {
        var options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[] { new FieldTransformRuleOptions { Type = "Set", Field = "Custom.Any", Enabled = true } }
                }
            }
        };

        // No factory — field-existence check is skipped
        var sut = BuildValidator(options);
        var report = await sut.ValidateAsync(sampleSize: 1, cancellationToken: CancellationToken.None);

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.Entries.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_WithEmptyType_ReturnsErrorEntry()
    {
        var options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Name = "Group1",
                    Enabled = true,
                    Transforms = new[] { new FieldTransformRuleOptions { Type = string.Empty, Enabled = true } }
                }
            }
        };

        var sut = BuildValidator(options);
        var report = await sut.ValidateAsync(cancellationToken: CancellationToken.None);

        Assert.IsFalse(report.IsValid);
        Assert.AreEqual(1, report.Entries.Count);
        Assert.AreEqual(FieldTransformValidationSeverity.Error, report.Entries[0].Severity);
        Assert.AreEqual("Group1", report.Entries[0].GroupName);
    }

    [TestMethod]
    public async Task ValidateAsync_WithProviderFactory_WhenFieldExists_ReturnsValidReport()
    {
        var options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[]
                    {
                        new FieldTransformRuleOptions
                        {
                            Type = "Set",
                            Field = "System.Title",
                            Enabled = true
                        }
                    }
                }
            }
        };

        var provider = new Mock<IFieldDefinitionProvider>(MockBehavior.Strict);
        provider
            .Setup(p => p.GetFieldDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldDefinition>
            {
                new FieldDefinition("System.Title", "Title", "String", false, null)
            });

        var factory = new Mock<IFieldDefinitionProviderFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.Create("source"))
            .Returns(provider.Object);

        var sut = BuildValidator(options, factory.Object);
        var report = await sut.ValidateAsync(cancellationToken: CancellationToken.None);

        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(0, report.Entries.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_WithProviderFactory_WhenFieldMissing_ReturnsErrorEntry()
    {
        var options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = new[]
            {
                new FieldTransformGroupOptions
                {
                    Enabled = true,
                    Transforms = new[]
                    {
                        new FieldTransformRuleOptions
                        {
                            Type = "Set",
                            Field = "Custom.NonExistent",
                            Enabled = true
                        }
                    }
                }
            }
        };

        var provider = new Mock<IFieldDefinitionProvider>(MockBehavior.Strict);
        provider
            .Setup(p => p.GetFieldDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FieldDefinition>
            {
                new FieldDefinition("System.Title", "Title", "String", false, null)
            });

        var factory = new Mock<IFieldDefinitionProviderFactory>(MockBehavior.Strict);
        factory
            .Setup(f => f.Create("source"))
            .Returns(provider.Object);

        var sut = BuildValidator(options, factory.Object);
        var report = await sut.ValidateAsync(cancellationToken: CancellationToken.None);

        Assert.IsFalse(report.IsValid);
        Assert.AreEqual(1, report.Entries.Count);
        Assert.AreEqual(FieldTransformValidationSeverity.Error, report.Entries[0].Severity);
        Assert.AreEqual("Custom.NonExistent", report.Entries[0].Field);
        StringAssert.Contains(report.Entries[0].Message, "Custom.NonExistent");
    }
}
