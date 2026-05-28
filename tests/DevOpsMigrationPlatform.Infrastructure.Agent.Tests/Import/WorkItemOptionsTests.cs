// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class WorkItemOptionsTests
{
    [TestMethod]
    public void WorkItemOptions_UsesExpectedSectionName()
    {
        Assert.AreEqual("Extensions:WorkItem", WorkItemOptions.SectionName);
    }

    [TestMethod]
    public void WorkItemOptions_DefaultFlags_AreFalse()
    {
        var sut = new WorkItemOptions();

        Assert.IsFalse(sut.RevisionReplay);
        Assert.IsFalse(sut.LinkReplay);
        Assert.IsFalse(sut.AttachmentReplay);
        Assert.IsFalse(sut.EmbeddedImageReplay);
        Assert.IsFalse(sut.FieldTransform);
    }
}
