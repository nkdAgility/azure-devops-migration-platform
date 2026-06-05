// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Identity;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Identity;

/// <summary>
/// Live integration test for <c>AzureDevOpsIdentityAdapter</c> against a real Azure DevOps
/// target tenant (operator decision: the ADO adapter's correctness is verified by a live
/// SystemTest, since the ADO SDK identity types are not cleanly unit-mockable).
/// Skips (Inconclusive) when the required environment is not configured, so it never fails
/// the normal build gate.
/// </summary>
[TestClass]
public sealed class AzureDevOpsIdentityAdapterSystemTests
{
    private static AzureDevOpsIdentityAdapter? CreateAdapterOrSkip(out string upn, out string displayName)
    {
        var org = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var pat = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");
        upn = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_IDENTITY_UPN") ?? string.Empty;
        displayName = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_IDENTITY_DISPLAYNAME") ?? string.Empty;

        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(pat) || string.IsNullOrEmpty(upn))
        {
            Assert.Fail(
                "Live ADO identity system test skipped: set AZDEVOPS_SYSTEM_TEST_ORG, AZDEVOPS_SYSTEM_TEST_PAT, " +
                "and AZDEVOPS_SYSTEM_TEST_IDENTITY_UPN (optionally AZDEVOPS_SYSTEM_TEST_IDENTITY_DISPLAYNAME).");
            return null;
        }

        var endpoint = new OrganisationEndpoint
        {
            ResolvedUrl = org,
            Type = "AzureDevOpsServices",
            Authentication = new OrganisationEndpointAuthentication { ResolvedAccessToken = pat },
        };

        var target = new Mock<ITargetEndpointInfo>();
        target.Setup(t => t.ToOrganisationEndpoint()).Returns(endpoint);
        target.SetupGet(t => t.ConnectorType).Returns("AzureDevOpsServices");
        target.SetupGet(t => t.Project).Returns(string.Empty);

        return new AzureDevOpsIdentityAdapter(
            new AzureDevOpsClientFactory(),
            target.Object,
            NullLogger<AzureDevOpsIdentityAdapter>.Instance);
    }

    [TestMethod]
    [TestCategory("SystemTest_Live")]
    public async Task FindByUpnAsync_Live_ReturnsMatchingCandidate()
    {
        var adapter = CreateAdapterOrSkip(out var upn, out _);
        if (adapter is null) return;

        var candidates = await adapter.FindByUpnAsync(upn, string.Empty, CancellationToken.None);

        Assert.IsNotNull(candidates);
        Assert.IsTrue(candidates.Count >= 1, $"Expected at least one target candidate for UPN '{upn}'.");
    }

    [TestMethod]
    [TestCategory("SystemTest_Live")]
    public async Task FindByDisplayNameAsync_Live_ReturnsWithoutThrowing()
    {
        var adapter = CreateAdapterOrSkip(out _, out var displayName);
        if (adapter is null) return;
        if (string.IsNullOrEmpty(displayName))
        {
            Assert.Fail("Set AZDEVOPS_SYSTEM_TEST_IDENTITY_DISPLAYNAME to exercise display-name search.");
            return;
        }

        var candidates = await adapter.FindByDisplayNameAsync(displayName, string.Empty, CancellationToken.None);

        Assert.IsNotNull(candidates);
        Assert.IsTrue(candidates.Count >= 1, $"Expected at least one target candidate for display name '{displayName}'.");
    }
}
