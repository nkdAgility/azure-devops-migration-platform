// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests.Nodes;

/// <summary>
/// #169 live E2E: Azure DevOps answers a lookup for a missing area or iteration with
/// VS402485 ("The Area/Iteration name is not recognized"). That is "does not exist", not a crash.
/// </summary>
[TestClass]
public class AzureDevOpsNodeCreatorNotFoundTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsNotFound_AreaOrIterationNameNotRecognized_IsNotFound()
    {
        var ex = new Exception(@"VS402485: The Area/Iteration name is not recognized: ProjectA\Team A");

        Assert.IsTrue(AzureDevOpsNodeCreator.IsNotFound(ex));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void IsNotFound_Unauthorized_IsNotNotFound()
    {
        Assert.IsFalse(AzureDevOpsNodeCreator.IsNotFound(new Exception("TF400813: The user is not authorized to access this resource.")));
    }
}
