// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

/// <summary>
/// EC-M2 / ADR-0024 (operator-consented guardrail-challenge outcome): the ADO
/// board/backlog/identity list operations issue single unpaged SDK calls because the
/// underlying REST endpoints (api-version 7.1) expose no pagination parameters. The
/// mandatory-pagination rule is satisfied by documented exemptions — these tests pin
/// that every exempted call site carries the exemption marker and that the connector
/// contract documents the exemptions with API evidence.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("UnitTests")]
public sealed class AdoPaginationExemptionContractTests
{
    [TestMethod]
    public void ExemptedCallSites_CarryPaginationExemptionMarker()
    {
        var repoRoot = FindRepoRoot();

        var boardAdapter = File.ReadAllText(Path.Combine(repoRoot,
            "src", "DevOpsMigrationPlatform.Infrastructure.AzureDevOps", "Teams", "AzureDevOpsBoardAdapter.cs"));
        Assert.AreEqual(3, CountOccurrences(boardAdapter, "PAGINATION EXEMPTION (ADR-0024, EC-M2)"),
            "AzureDevOpsBoardAdapter must document the boards/backlogs/taskboard-columns pagination exemptions.");

        var identityAdapter = File.ReadAllText(Path.Combine(repoRoot,
            "src", "DevOpsMigrationPlatform.Infrastructure.AzureDevOps", "Identity", "AzureDevOpsIdentityAdapter.cs"));
        Assert.AreEqual(1, CountOccurrences(identityAdapter, "PAGINATION EXEMPTION (ADR-0024, EC-M2)"),
            "AzureDevOpsIdentityAdapter must document the ReadIdentities pagination exemption.");
    }

    [TestMethod]
    public void ConnectorModelContract_DocumentsPaginationExemptionsWithApiEvidence()
    {
        var repoRoot = FindRepoRoot();
        var doc = File.ReadAllText(Path.Combine(repoRoot, ".agents", "30-context", "domains", "connector-model.md"));

        StringAssert.Contains(doc, "Pagination Exemptions");
        StringAssert.Contains(doc, "work/boards/list?view=azure-devops-rest-7.1");
        StringAssert.Contains(doc, "work/backlogs/list?view=azure-devops-rest-7.1");
        StringAssert.Contains(doc, "ims/identities/read-identities?view=azure-devops-rest-7.1");
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DevOpsMigrationPlatform.slnx")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "Could not locate repository root.");
        return dir!.FullName;
    }
}
