// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Xml;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Nodes;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// #169 live E2E found two TFS Object Model export defects that stop a TFS → Azure DevOps
/// migration exporting anything: work items created today were never queried, and the
/// classification tree export only looked at the two root structures.
/// </summary>
[TestClass]
public class TfsObjectModelExportDefectTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void FirstCreatedDateWindowEnd_IncludesWorkItemsCreatedToday()
    {
        var now = new DateTime(2026, 11, 16, 16, 5, 0, DateTimeKind.Utc);

        var end = WorkItemStoreExtensions.FirstCreatedDateWindowEnd(now);

        // WIQL compares CreatedDate by day: the window must end after today, not at today 00:00.
        Assert.AreEqual(new DateTime(2026, 11, 17), end.Date);
        Assert.IsTrue(end > now);
    }

    private static XmlElement NodesXml(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc.DocumentElement!;
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ClassificationNodesXml_Flatten_ReturnsRootAndEveryChild_WithoutLeadingBackslash()
    {
        var xml = NodesXml("""
            <Nodes>
              <Node NodeID="a1" Name="Area" Path="\P\Area" StructureType="ProjectModelHierarchy">
                <Children>
                  <Node NodeID="a2" Name="Team A" Path="\P\Area\Team A">
                    <Children><Node NodeID="a3" Name="Squad" Path="\P\Area\Team A\Squad" /></Children>
                  </Node>
                </Children>
              </Node>
            </Nodes>
            """);

        var paths = ClassificationNodesXml.Flatten(xml).Select(n => n.Path).ToArray();

        CollectionAssert.AreEqual(new[] { @"P\Area", @"P\Area\Team A", @"P\Area\Team A\Squad" }, paths);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ClassificationNodesXml_Flatten_ReadsIterationDates()
    {
        var xml = NodesXml("""
            <Nodes>
              <Node NodeID="i1" Name="Iteration" Path="\P\Iteration" StructureType="ProjectLifecycle">
                <Children>
                  <Node NodeID="i2" Name="Sprint 1" Path="\P\Iteration\Sprint 1" StartDate="2026-11-02T00:00:00Z" FinishDate="2026-11-13T00:00:00Z" />
                </Children>
              </Node>
            </Nodes>
            """);

        var sprint = ClassificationNodesXml.Flatten(xml).Single(n => n.Path == @"P\Iteration\Sprint 1");

        Assert.AreEqual(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), sprint.StartDate);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero), sprint.FinishDate);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ClassificationNodesXml_RecognisesTheRootStructureTypesTfsReturns()
    {
        Assert.IsTrue(ClassificationNodesXml.IsAreaRoot("ProjectModelHierarchy"));
        Assert.IsTrue(ClassificationNodesXml.IsIterationRoot("ProjectLifecycle"));
        Assert.IsFalse(ClassificationNodesXml.IsAreaRoot("ProjectLifecycle"));
        Assert.IsFalse(ClassificationNodesXml.IsIterationRoot("ProjectModelHierarchy"));
    }
}
