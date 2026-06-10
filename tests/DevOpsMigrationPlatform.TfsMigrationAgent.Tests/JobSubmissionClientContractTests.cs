// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Jobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

[TestClass]
public class JobSubmissionClientContractTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Interface_IsAvailable_ForNet481Build()
    {
        Assert.IsNotNull(typeof(IJobSubmissionClient));
    }
}
