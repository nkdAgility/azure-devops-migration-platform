// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Architecture;

/// <summary>
/// Convention tests for the Tool taxonomy ruling (ADR-0026 amendment, TC-H1):
/// Tools are pure, stateless transformation engines; units that perform package
/// I/O or replay orchestration are services and must not carry the Tool name.
/// </summary>
[TestClass]
public sealed class ToolTaxonomyArchitectureTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void WorkItemsModule_ContainsNoTypeNamedTool()
    {
        // TC-H1: replay units under WorkItems do I/O and are therefore services.
        // Nothing named *Tool may live under Infrastructure.Agent/WorkItems —
        // pure engines belong under Infrastructure.Agent/Tools.
        var workItemsRoot = Path.Combine(
            FindRepoRoot(), "src", "DevOpsMigrationPlatform.Infrastructure.Agent", "WorkItems");

        Assert.IsTrue(Directory.Exists(workItemsRoot), $"Expected directory at {workItemsRoot}.");

        var toolFiles = Directory
            .EnumerateFiles(workItemsRoot, "*Tool.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.AreEqual(
            0,
            toolFiles.Count,
            $"WorkItems module must not contain types named *Tool (TC-H1 / ADR-0026 amendment). Found: {string.Join(", ", toolFiles)}");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AttachmentReplayService_ExistsAndOldToolNameIsGone()
    {
        // TC-H1 resolution pin: AttachmentReplayTool was renamed to
        // AttachmentReplayService per the operator's taxonomy ruling.
        var attachmentsRoot = Path.Combine(
            FindRepoRoot(), "src", "DevOpsMigrationPlatform.Infrastructure.Agent",
            "WorkItems", "Attachments");

        Assert.IsTrue(
            File.Exists(Path.Combine(attachmentsRoot, "AttachmentReplayService.cs")),
            "AttachmentReplayService.cs must exist under WorkItems/Attachments (TC-H1).");
        Assert.IsFalse(
            File.Exists(Path.Combine(attachmentsRoot, "AttachmentReplayTool.cs")),
            "AttachmentReplayTool.cs must no longer exist (renamed per TC-H1 ruling).");

        var serviceType = typeof(DevOpsMigrationPlatform.Infrastructure.Agent
            .WorkItems.Attachments.AttachmentReplayService);
        Assert.IsNotNull(serviceType);

        var oldType = serviceType.Assembly.GetType(
            "DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.AttachmentReplayTool");
        Assert.IsNull(oldType, "Type AttachmentReplayTool must not exist in Infrastructure.Agent (TC-H1).");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevOpsMigrationPlatform.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root walking up from {AppContext.BaseDirectory}.");
    }
}
