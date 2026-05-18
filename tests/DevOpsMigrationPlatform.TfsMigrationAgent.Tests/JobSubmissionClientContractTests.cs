// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
[TestCategory("NET481")]
public class JobSubmissionClientContractTests
{
    [TestMethod]
    public void Interface_IsAvailable_ForNet481Build()
    {
        Assert.IsNotNull(typeof(IJobSubmissionClient));
    }
}
