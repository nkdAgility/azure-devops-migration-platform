// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

/// <summary>
/// #169 live E2E: the TFS agent never set the job's source endpoint with its credentials, so
/// embedded-image downloads went out unauthenticated (203 sign-in page). Both agents now build the
/// job's source and target endpoints from its config in one shared place.
/// </summary>
[TestClass]
public class ConfiguredJobEndpointsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in values) dict[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TryBuildSource_ResolvesTokenProjectAndSlug()
    {
        Environment.SetEnvironmentVariable("E2E169_SOURCE_TOKEN", "tok-169");
        try
        {
            var config = Config(
                ("MigrationPlatform:Source:Type", "TeamFoundationServer"),
                ("MigrationPlatform:Source:Url", "https://dev.azure.com/source-org"),
                ("MigrationPlatform:Source:Project", "SourceProject"),
                ("MigrationPlatform:Source:Authentication:Type", "AccessToken"),
                ("MigrationPlatform:Source:Authentication:AccessToken", "$ENV:E2E169_SOURCE_TOKEN"));

            Assert.IsTrue(ConfiguredJobEndpoints.TryBuildSource(config, out var source));

            Assert.AreEqual("https://dev.azure.com/source-org", source.Url);
            Assert.AreEqual("SourceProject", source.Project);
            Assert.AreEqual("TeamFoundationServer", source.ConnectorType);
            Assert.AreEqual("source-org", source.OrganisationSlug);
            var auth = source.ToOrganisationEndpoint().Authentication;
            Assert.AreEqual(AuthenticationType.AccessToken, auth.Type);
            Assert.AreEqual("tok-169", auth.ResolvedAccessToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("E2E169_SOURCE_TOKEN", null);
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TryBuildSource_NoSourceType_ReturnsFalse()
    {
        Assert.IsFalse(ConfiguredJobEndpoints.TryBuildSource(Config(("MigrationPlatform:Source:Url", "https://x")), out _));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TryBuildSource_MoreThanOneGeneratedProject_IsRejected()
    {
        // Martin, 2026-11-17: multi-project support is not approved or allowed. A job migrates
        // one source project, so a second generated project is an error, not silently ignored.
        var config = Config(
            ("MigrationPlatform:Source:Type", "Simulated"),
            ("MigrationPlatform:Source:Generator:Projects:0:Name", "Alpha"),
            ("MigrationPlatform:Source:Generator:Projects:1:Name", "Beta"));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => ConfiguredJobEndpoints.TryBuildSource(config, out _));
        StringAssert.Contains(ex.Message, "one source project");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TryBuildSource_OneGeneratedProject_IsTheSourceProject()
    {
        var config = Config(
            ("MigrationPlatform:Source:Type", "Simulated"),
            ("MigrationPlatform:Source:Generator:Projects:0:Name", "Alpha"));

        Assert.IsTrue(ConfiguredJobEndpoints.TryBuildSource(config, out var source));
        Assert.AreEqual("Alpha", source.Project);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TryBuildTarget_ResolvesProject()
    {
        var config = Config(
            ("MigrationPlatform:Target:Type", "AzureDevOpsServices"),
            ("MigrationPlatform:Target:Url", "https://dev.azure.com/target-org"),
            ("MigrationPlatform:Target:Project", "TargetProject"));

        Assert.IsTrue(ConfiguredJobEndpoints.TryBuildTarget(config, out var target));
        Assert.AreEqual("TargetProject", target.Project);
        Assert.AreEqual("target-org", target.OrganisationSlug);
    }
}
