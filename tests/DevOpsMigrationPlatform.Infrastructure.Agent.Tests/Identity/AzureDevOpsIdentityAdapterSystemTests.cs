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
/// Skips cleanly (passes as a no-op) when the live identity test data is not configured,
/// so it never fails the build or live gate when the credentials/identity vars are absent.
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
            // Live identity test data is environment-specific and is not provisioned in the
            // shared CI live gate. Skip cleanly (no-op pass) rather than failing the gate; set
            // AZDEVOPS_SYSTEM_TEST_ORG, AZDEVOPS_SYSTEM_TEST_PAT, and
            // AZDEVOPS_SYSTEM_TEST_IDENTITY_UPN locally to exercise it.
            Console.WriteLine(
                "[SystemTest_Live] AzureDevOpsIdentityAdapter live test skipped — identity test data not configured.");
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

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [TestMethod]
    public async Task FindByUpnAsync_Live_ReturnsMatchingCandidate()
    {
        var adapter = CreateAdapterOrSkip(out var upn, out _);
        if (adapter is null) return;

        var candidates = await adapter.FindByUpnAsync(upn, string.Empty, CancellationToken.None);

        Assert.IsNotNull(candidates);
        Assert.IsTrue(candidates.Count >= 1, $"Expected at least one target candidate for UPN '{upn}'.");
    }

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [TestMethod]
    public async Task FindByDisplayNameAsync_Live_ReturnsWithoutThrowing()
    {
        var adapter = CreateAdapterOrSkip(out _, out var displayName);
        if (adapter is null) return;
        if (string.IsNullOrEmpty(displayName))
        {
            // Optional display-name probe data not configured — skip cleanly.
            Console.WriteLine(
                "[SystemTest_Live] Display-name search skipped — AZDEVOPS_SYSTEM_TEST_IDENTITY_DISPLAYNAME not set.");
            return;
        }

        var candidates = await adapter.FindByDisplayNameAsync(displayName, string.Empty, CancellationToken.None);

        Assert.IsNotNull(candidates);
        Assert.IsTrue(candidates.Count >= 1, $"Expected at least one target candidate for display name '{displayName}'.");
    }
}
