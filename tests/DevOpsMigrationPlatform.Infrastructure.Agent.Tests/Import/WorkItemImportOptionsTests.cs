// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class WorkItemImportOptionsTests
{
    [TestMethod]
    public void WorkItemImportOptions_DefaultFlags_AreFalse()
    {
        var sut = new WorkItemImportOptions();

        Assert.IsFalse(sut.RevisionReplay);
        Assert.IsFalse(sut.LinkReplay);
        Assert.IsFalse(sut.AttachmentReplay);
        Assert.IsFalse(sut.EmbeddedImageReplay);
        Assert.IsFalse(sut.FieldTransform);
    }
}
