// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class IdentityMappingFindingTests
{
    [TestMethod]
    public void Constructor_SetsExpectedFieldValues()
    {
        var finding = new IdentityMappingFinding(
            "aad.1234",
            "Jane Doe",
            IdentityMappingFindingStatus.Unmapped,
            "user@contoso.com",
            IdentityMappingOperatorDecision.UseDefault);

        Assert.AreEqual("aad.1234", finding.SourceId);
        Assert.AreEqual("Jane Doe", finding.SourceDisplay);
        Assert.AreEqual(IdentityMappingFindingStatus.Unmapped, finding.Status);
        Assert.AreEqual("user@contoso.com", finding.TargetId);
        Assert.AreEqual(IdentityMappingOperatorDecision.UseDefault, finding.OperatorDecision);
    }
}
