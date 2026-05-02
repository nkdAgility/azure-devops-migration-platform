// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

[TestClass]
public class TokenResolverTests
{
    // ── Null input ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Resolve_NullInput_ReturnsNull()
    {
        var result = ConfigTokenResolver.Resolve(null);
        Assert.IsNull(result);
    }

    // ── Literal values ────────────────────────────────────────────────────────

    [TestMethod]
    public void Resolve_LiteralToken_ReturnsSameValue()
    {
        const string token = "my-literal-pat-token";
        var result = ConfigTokenResolver.Resolve(token);
        Assert.AreEqual(token, result);
    }

    [TestMethod]
    public void Resolve_EmptyString_ReturnsNull()
    {
        var result = ConfigTokenResolver.Resolve(string.Empty);
        Assert.IsNull(result);
    }

    // ── ENV var resolution ────────────────────────────────────────────────────

    [TestMethod]
    public void Resolve_EnvVarPrefix_ReturnsEnvVarValue()
    {
        const string varName = "INVENTORY_TEST_PAT_TOKEN";
        Environment.SetEnvironmentVariable(varName, "resolved-pat-value");
        try
        {
            var result = ConfigTokenResolver.Resolve($"$ENV:{varName}");
            Assert.AreEqual("resolved-pat-value", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [TestMethod]
    public void Resolve_EnvVarPrefix_UnsetVariable_Throws()
    {
        const string varName = "INVENTORY_TEST_UNSET_TOKEN_XYZ";
        Environment.SetEnvironmentVariable(varName, null);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => ConfigTokenResolver.Resolve($"$ENV:{varName}"));
    }

    [TestMethod]
    public void Resolve_EnvVarPrefixLowercase_AlsoResolvesVariable()
    {
        // $ENV: is case-insensitive — $env: must also trigger env var resolution
        const string varName = "INVENTORY_TEST_LOWERCASE_TOKEN";
        Environment.SetEnvironmentVariable(varName, "lower-resolved");
        try
        {
            var result = ConfigTokenResolver.Resolve($"$env:{varName}");
            Assert.AreEqual("lower-resolved", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }
}
