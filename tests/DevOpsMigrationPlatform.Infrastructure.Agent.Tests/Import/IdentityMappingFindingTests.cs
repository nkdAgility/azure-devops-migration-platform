// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Import;
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
            IdentityMappingFindingStatus.Unresolved,
            "user@contoso.com",
            "UseDefault");

        Assert.AreEqual("aad.1234", finding.SourceIdentityId);
        Assert.AreEqual(IdentityMappingFindingStatus.Unresolved, finding.Status);
        Assert.AreEqual("user@contoso.com", finding.TargetReference);
        Assert.AreEqual("UseDefault", finding.OperatorDecision);
    }
}
