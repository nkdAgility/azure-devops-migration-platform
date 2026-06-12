// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems.Extensions;

[TestClass]
public sealed class LinksWorkItemExtensionTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Contract_DeclaresWorkItemsLinksImportOnly()
    {
        var ext = new LinksWorkItemExtension(Options.Create(new LinksExtensionOptions()));

        Assert.AreEqual("WorkItems", ext.Module);
        Assert.AreEqual("Links", ext.Name);
        Assert.IsTrue(ext.SupportsImport);
        Assert.IsFalse(ext.SupportsExport);
        Assert.IsTrue(ext.IsEnabled);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsEnabled_ReflectsOwnOptions()
    {
        var ext = new LinksWorkItemExtension(Options.Create(new LinksExtensionOptions { Enabled = false }));
        Assert.IsFalse(ext.IsEnabled);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_AddsRevisionLinksToResolvedTarget()
    {
        var related = new[] { new RelatedWorkItemLink { ArtifactLinkType = "System.LinkTypes.Related", LinkTypeEnd = "Related", RelatedWorkItemId = 7 } };
        var external = new[] { new ExternalWorkItemLink { ArtifactLinkType = "Hyperlink", LinkedArtifactUri = "http://ext/x" } };
        var hyper = new[] { new HyperlinkWorkItemLink { ArtifactLinkType = "Hyperlink", Location = "http://h/y" } };

        var target = new Mock<IWorkItemTarget>(MockBehavior.Strict);
        target
            .Setup(t => t.AddLinksAsync(4242, related, external, hyper, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var ext = new LinksWorkItemExtension(Options.Create(new LinksExtensionOptions()));

        await ext.ImportAsync(CreateContext(related, external, hyper, targetId: 4242, target: target.Object), CancellationToken.None);

        target.Verify();
    }

    private static WorkItemExtensionContext CreateContext(
        IReadOnlyList<RelatedWorkItemLink> related,
        IReadOnlyList<ExternalWorkItemLink> external,
        IReadOnlyList<HyperlinkWorkItemLink> hyper,
        int targetId,
        IWorkItemTarget? target = null)
        => new()
        {
            Organisation = "org",
            ProjectName = "proj",
            EntityId = "42",
            TargetEntityId = targetId.ToString(),
            Package = Mock.Of<IPackageAccess>(),
            TargetWorkItemId = targetId,
            FolderPath = "WorkItems/2026-01-15/1-42-3",
            Target = target,
            Revision = new WorkItemRevision
            {
                WorkItemId = 42,
                RevisionIndex = 3,
                ChangedDate = DateTimeOffset.UtcNow,
                RelatedLinks = related,
                ExternalLinks = external,
                Hyperlinks = hyper
            }
        };
}
