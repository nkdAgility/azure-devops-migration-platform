// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Reflection;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Abstractions.Tests.Jobs;

[TestClass]
public class IJobSubmissionClientContractTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void RunAsync_HasExpectedSignature()
    {
        var method = typeof(IJobSubmissionClient).GetMethod(nameof(IJobSubmissionClient.RunAsync));

        Assert.IsNotNull(method);
        Assert.AreEqual(typeof(IAsyncEnumerable<ProgressEvent>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.AreEqual(2, parameters.Length);
        Assert.AreEqual(typeof(Job), parameters[0].ParameterType);
        Assert.AreEqual(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.IsTrue(parameters[1].HasDefaultValue);
    }
}
