// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class WorkItemsPrepareRevisionReaderTests
{
    [TestMethod]
    public async Task EnumerateAsync_LegacyModuleOnlyLayout_FallsBackAndReadsRevision()
    {
        using var package = new InMemoryPackageAccess();
        await package.WriteAsync(
            "WorkItems/638800000000000000/123/1/revision.json",
            "{\"workItemId\":123,\"revisionIndex\":1}",
            CancellationToken.None);

        var results = new List<ParsedWorkItemRevision>();
        await foreach (var item in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           package,
                           "fabrikam",
                           "migration",
                           CancellationToken.None))
        {
            results.Add(item);
        }

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("WorkItems/638800000000000000/123/1/revision.json", results[0].RevisionJsonPath);
        Assert.IsNotNull(results[0].Revision);
        Assert.IsNull(results[0].ParseError);
    }
}
