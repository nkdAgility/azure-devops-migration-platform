// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryAbstractionsPlacementTests
{
    [TestMethod]
    public void PackageBoundaryContracts_AreDefinedOnlyInAbstractionsAgent()
    {
        var contractsAssembly = PackageBoundaryTestFixture.ContractsAssemblyName;
        Assert.IsNotNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Agent.Storage.IPackage, {contractsAssembly}"));
        Assert.IsNotNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Agent.Storage.PackageContext, {contractsAssembly}"));
        Assert.IsNotNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Agent.Storage.PackageMetaContext, {contractsAssembly}"));
        Assert.IsNotNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Agent.Storage.PackageLogContext, {contractsAssembly}"));
    }

    [TestMethod]
    public void PackageBoundaryContracts_AreNotDefinedInHigherAbstractionsAssembly()
    {
        const string higherAssembly = "DevOpsMigrationPlatform.Abstractions";
        Assert.IsNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.IPackage, {higherAssembly}"));
        Assert.IsNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageContext, {higherAssembly}"));
        Assert.IsNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageMetaContext, {higherAssembly}"));
        Assert.IsNull(Type.GetType($"DevOpsMigrationPlatform.Abstractions.Storage.PackageLogContext, {higherAssembly}"));
    }
}

