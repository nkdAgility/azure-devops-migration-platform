// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Platform;

[Binding]
public sealed class PackageBoundaryAdoptionSteps
{
    private Type? _packageContractType;

    [Given("the package boundary contract surface is required")]
    public void GivenThePackageBoundaryContractSurfaceIsRequired()
    {
        // Intentional RED baseline: contract type does not exist before implementation.
        _packageContractType = Type.GetType("DevOpsMigrationPlatform.Abstractions.Agent.Storage.IPackageAccess, DevOpsMigrationPlatform.Abstractions.Agent");
    }

    [When("I validate the package boundary contract availability")]
    public void WhenIValidateThePackageBoundaryContractAvailability()
    {
        // No-op: state was collected in Given.
    }

    [Then("package boundary contracts are available to callers")]
    public void ThenPackageBoundaryContractsAreAvailableToCallers()
    {
        Assert.IsNotNull(_packageContractType);
    }
}

