// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.WorkItems;

[TestClass]
public sealed class WorkItemCadenceTracingTests
{
    [TestMethod]
    public void PackagePathResolution_EmitsStateProgressTrace()
    {
        var spans = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DevOpsMigrationPlatform.Migration",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => spans.Add(activity.DisplayName)
        };
        ActivitySource.AddActivityListener(listener);

        _ = PackagePaths.ContinuationFile("export", "workitems", "https://dev.azure.com/contoso", "Shop");
        CollectionAssert.Contains(spans, "state.paths.resolve");
    }
}
