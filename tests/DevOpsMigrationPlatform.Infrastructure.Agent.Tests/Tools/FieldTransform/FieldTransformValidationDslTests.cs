// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform;

[TestClass]
public class FieldTransformValidationDslTests
{
    [TestMethod]
    public async Task ValidateAsync_WithExistingFieldReferences_ReportsSuccess()
    {
        var result = await FieldTransformValidationScenario
            .Create()
            .WithSourceField("System.Title", "String")
            .WithSetTransform("System.Title")
            .RunAsync();

        result.ShouldBeValid();
    }

    [TestMethod]
    public async Task ValidateAsync_WithMissingFieldReference_ReportsError()
    {
        var result = await FieldTransformValidationScenario
            .Create()
            .WithSourceField("System.Title", "String")
            .WithSetTransform("Custom.NonExistent")
            .RunAsync();

        result.ShouldContainErrorForField("Custom.NonExistent");
    }

    [TestMethod]
    public async Task ValidateAsync_WithTypeMismatchStyleConfiguration_CompletesWithReport()
    {
        var result = await FieldTransformValidationScenario
            .Create()
            .WithSourceField("System.Title", "String")
            .WithCopyTransform("System.Title", "System.Title")
            .RunAsync();

        result.ShouldHaveEntriesCollection();
    }

    [TestMethod]
    public async Task ValidateAsync_WithPicklistMappingStyleConfiguration_CompletesWithReport()
    {
        var result = await FieldTransformValidationScenario
            .Create()
            .WithSourceField("System.State", "String", ["Active", "Resolved"])
            .WithMapValueTransform("System.State", "Active", "NonExistentState")
            .RunAsync();

        result.ShouldHaveEntriesCollection();
    }

    [TestMethod]
    public async Task ValidateAsync_WithSampleSize_CompletesDryRunAsValid()
    {
        var result = await FieldTransformValidationScenario
            .Create()
            .WithSourceField("System.Title", "String")
            .WithSetTransform("System.Title")
            .RunAsync(sampleSize: 5);

        result.ShouldBeValid();
    }
}

internal sealed class FieldTransformValidationScenario
{
    private readonly List<FieldDefinition> _sourceFields = [];
    private readonly List<FieldTransformGroupOptions> _groups = [];

    private FieldTransformValidationScenario()
    {
    }

    public static FieldTransformValidationScenario Create() => new();

    public FieldTransformValidationScenario WithSourceField(string referenceName, string type, IReadOnlyList<string>? allowedValues = null)
    {
        _sourceFields.Add(new FieldDefinition(referenceName, referenceName, type, false, allowedValues));
        return this;
    }

    public FieldTransformValidationScenario WithSetTransform(string field)
    {
        _groups.Add(new FieldTransformGroupOptions
        {
            Enabled = true,
            Transforms =
            [
                new FieldTransformRuleOptions { Type = "Set", Field = field, Enabled = true },
            ],
        });
        return this;
    }

    public FieldTransformValidationScenario WithCopyTransform(string sourceField, string targetField)
    {
        _groups.Add(new FieldTransformGroupOptions
        {
            Enabled = true,
            Transforms =
            [
                new FieldTransformRuleOptions { Type = "Copy", SourceField = sourceField, TargetField = targetField, Enabled = true },
            ],
        });
        return this;
    }

    public FieldTransformValidationScenario WithMapValueTransform(string field, string sourceValue, string targetValue)
    {
        _groups.Add(new FieldTransformGroupOptions
        {
            Enabled = true,
            Transforms =
            [
                new FieldTransformRuleOptions
                {
                    Type = "MapValue",
                    Field = field,
                    Enabled = true,
                    ValueMap = new Dictionary<string, string> { [sourceValue] = targetValue },
                },
            ],
        });
        return this;
    }

    public async Task<FieldTransformValidationResult> RunAsync(int sampleSize = 10)
    {
        var options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = _groups,
        };

        var sourceProvider = new Mock<IFieldDefinitionProvider>(MockBehavior.Strict);
        sourceProvider
            .Setup(p => p.GetFieldDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sourceFields);

        var providerFactory = new Mock<IFieldDefinitionProviderFactory>(MockBehavior.Strict);
        providerFactory
            .Setup(f => f.Create("source"))
            .Returns(sourceProvider.Object);

        var validator = new FieldTransformValidator(
            Options.Create(options),
            NullLogger<FieldTransformValidator>.Instance,
            providerFactory.Object);

        var report = await validator.ValidateAsync(sampleSize, CancellationToken.None);
        return new FieldTransformValidationResult(report);
    }
}

internal readonly record struct FieldTransformValidationResult(FieldTransformValidationReport Report);

internal static class FieldTransformValidationAssertions
{
    public static void ShouldBeValid(this FieldTransformValidationResult result)
    {
        Assert.IsTrue(result.Report.IsValid, "Expected validation report to be valid.");
    }

    public static void ShouldContainErrorForField(this FieldTransformValidationResult result, string fieldName)
    {
        Assert.IsFalse(result.Report.IsValid, "Expected validation report to be invalid.");

        foreach (var entry in result.Report.Entries)
        {
            if (entry.Severity == FieldTransformValidationSeverity.Error &&
                entry.Field == fieldName)
            {
                return;
            }
        }

        Assert.Fail($"Expected an error entry for field '{fieldName}'.");
    }

    public static void ShouldHaveEntriesCollection(this FieldTransformValidationResult result)
    {
        Assert.IsNotNull(result.Report.Entries, "Expected entries collection to be present.");
    }
}
