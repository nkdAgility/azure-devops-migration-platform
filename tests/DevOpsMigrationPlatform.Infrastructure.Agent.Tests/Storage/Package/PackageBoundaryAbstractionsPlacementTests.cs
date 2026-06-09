// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryAbstractionsPlacementTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void PackageBoundaryContracts_AreDefinedOnlyInAbstractionsAgent()
    {
        var contractsAssembly = PackageBoundaryTestFixture.ContractsAssemblyName;
        Assert.IsNotNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.IPackageAccess, {contractsAssembly}"));
        Assert.IsNotNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageContentContext, {contractsAssembly}"));
        Assert.IsNotNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageMetaContext, {contractsAssembly}"));
        Assert.IsNotNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageLogContext, {contractsAssembly}"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void PackageBoundaryContracts_AreNotDefinedInHigherAbstractionsAssembly()
    {
        const string higherAssembly = "DevOpsMigrationPlatform.Abstractions";
        Assert.IsNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.IPackageAccess, {higherAssembly}"));
        Assert.IsNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageContentContext, {higherAssembly}"));
        Assert.IsNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageMetaContext, {higherAssembly}"));
        Assert.IsNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageLogContext, {higherAssembly}"));
    }
}

