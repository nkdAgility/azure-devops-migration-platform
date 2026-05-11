// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryContractRedTests
{
    [TestMethod]
    public void Red_IPackageContract_IsAvailableInAbstractionsAgent()
    {
        var ipackageType = Type.GetType(
            $"DevOpsMigrationPlatform.Abstractions.Agent.Storage.IPackageAccess, {PackageBoundaryTestFixture.ContractsAssemblyName}");

        Assert.IsNotNull(ipackageType);
    }
}

