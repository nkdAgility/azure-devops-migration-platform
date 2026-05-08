// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class PackagePathsTests
{
    [TestMethod]
    public void CursorFile_ActionQualified_UsesProjectScopedPath()
    {
        var path = PackagePaths.CursorFile("export", "workitems", "https://dev.azure.com/contoso", "Shopping");
        Assert.AreEqual("contoso/Shopping/.migration/export.workitems.cursor.json", path);
    }
}
