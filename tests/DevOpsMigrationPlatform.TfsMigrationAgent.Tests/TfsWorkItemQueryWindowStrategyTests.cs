// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// #171 live E2E: the configured Selection.Query must bound a TFS export. The date windows are
/// appended to the query as "AND [System.CreatedDate] ...", and the Object Model query has no
/// project context, so the query is reduced to a bracketed WHERE with @project resolved and
/// ORDER BY removed.
/// </summary>
[TestClass]
public class TfsWorkItemQueryWindowStrategyTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ToWindowBaseQuery_ConfiguredQuery_ResolvesProjectDropsOrderByAndBracketsTheWhere()
    {
        var baseQuery = TfsWorkItemQueryWindowStrategy.ToWindowBaseQuery(
            "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.WorkItemType] <> 'Issue' OR [System.Id] = 1 ORDER BY [System.Id]",
            "Team's Project");

        Assert.AreEqual(
            "SELECT [System.Id] FROM WorkItems WHERE ([System.TeamProject] = 'Team''s Project' AND [System.WorkItemType] <> 'Issue' OR [System.Id] = 1)",
            baseQuery);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ToWindowBaseQuery_NoQuery_IsTheWholeProject()
    {
        Assert.AreEqual(
            "SELECT [System.Id] FROM WorkItems WHERE ([System.TeamProject] = 'P')",
            TfsWorkItemQueryWindowStrategy.ToWindowBaseQuery(null, "P"));
    }
}
