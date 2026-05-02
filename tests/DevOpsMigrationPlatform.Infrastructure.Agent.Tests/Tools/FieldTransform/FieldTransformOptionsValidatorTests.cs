// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform;

/// <summary>
/// Unit tests for <see cref="FieldTransformOptionsValidator"/>.
/// </summary>
[TestClass]
public class FieldTransformOptionsValidatorTests
{
    private static FieldTransformOptionsValidator Sut() => new();

    private static FieldTransformOptions ValidOptions() => new()
    {
        TransformGroups = []
    };

    private static FieldTransformOptions WithRule(FieldTransformRuleOptions rule) => new()
    {
        TransformGroups = [new FieldTransformGroupOptions { Transforms = [rule] }]
    };

    // ── Passing cases ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_EmptyGroups_Succeeds()
    {
        var result = Sut().Validate(null, ValidOptions());
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_ValidRuleWithType_Succeeds()
    {
        var result = Sut().Validate(null, WithRule(new FieldTransformRuleOptions { Type = "SetField", Field = "System.Title", Value = "Migrated" }));
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_ValidRuleWithValidPattern_Succeeds()
    {
        var result = Sut().Validate(null, WithRule(new FieldTransformRuleOptions { Type = "RegexReplace", Field = "System.Title", Pattern = @"\bfoo\b", Replacement = "bar" }));
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_DisabledGroupSkipped_Succeeds()
    {
        var opts = new FieldTransformOptions
        {
            TransformGroups =
            [
                new FieldTransformGroupOptions
                {
                    Enabled = false,
                    Transforms = [new FieldTransformRuleOptions { Type = "" }]
                }
            ]
        };
        var result = Sut().Validate(null, opts);
        Assert.IsTrue(result.Succeeded, "Disabled groups should not be validated.");
    }

    [TestMethod]
    public void Validate_DisabledRuleSkipped_Succeeds()
    {
        var result = Sut().Validate(null, WithRule(new FieldTransformRuleOptions { Type = "", Enabled = false }));
        Assert.IsTrue(result.Succeeded, "Disabled rules should not be validated.");
    }

    // ── Failing cases ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_RuleMissingType_Fails()
    {
        var result = Sut().Validate(null, WithRule(new FieldTransformRuleOptions { Type = "" }));
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), ".Type is required");
    }

    [TestMethod]
    public void Validate_RuleNullType_Fails()
    {
        // Type defaults to string.Empty in the record, so simulate an unset rule
        var opts = new FieldTransformOptions
        {
            TransformGroups =
            [
                new FieldTransformGroupOptions
                {
                    Name = "MyGroup",
                    Transforms = [new FieldTransformRuleOptions { Type = "   " }]
                }
            ]
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "TransformGroups[0] ('MyGroup')");
    }

    [TestMethod]
    public void Validate_RuleWithInvalidPattern_Fails()
    {
        var result = Sut().Validate(null, WithRule(new FieldTransformRuleOptions { Type = "RegexReplace", Pattern = "(unclosed" }));
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), ".Pattern");
        StringAssert.Contains(string.Join("|", result.Failures!), "not a valid regular expression");
    }

    [TestMethod]
    public void Validate_MultipleRuleErrors_ReportsAll()
    {
        var opts = new FieldTransformOptions
        {
            TransformGroups =
            [
                new FieldTransformGroupOptions
                {
                    Transforms =
                    [
                        new FieldTransformRuleOptions { Type = "" },
                        new FieldTransformRuleOptions { Type = "RegexReplace", Pattern = "[bad" }
                    ]
                }
            ]
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        var failures = string.Join("|", result.Failures!);
        StringAssert.Contains(failures, "Transforms[0]");
        StringAssert.Contains(failures, "Transforms[1]");
    }
}
