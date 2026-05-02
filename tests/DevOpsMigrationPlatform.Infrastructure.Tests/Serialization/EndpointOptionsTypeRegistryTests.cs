// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Serialization;

[TestClass]
public sealed class EndpointOptionsTypeRegistryTests
{
    [TestMethod]
    public void Register_NewKey_Succeeds()
    {
        var registry = new EndpointOptionsTypeRegistry();
        registry.Register("TestKey", typeof(TestEndpointOptions));

        var found = registry.TryGetType("TestKey", out var type);
        Assert.IsTrue(found);
        Assert.AreEqual(typeof(TestEndpointOptions), type);
    }

    [TestMethod]
    public void Register_DuplicateKeyWithSameType_IsIdempotent()
    {
        var registry = new EndpointOptionsTypeRegistry();
        registry.Register("TestKey", typeof(TestEndpointOptions));
        // Should not throw
        registry.Register("TestKey", typeof(TestEndpointOptions));
    }

    [TestMethod]
    public void Register_DuplicateKeyWithDifferentType_ThrowsInvalidOperationException()
    {
        var registry = new EndpointOptionsTypeRegistry();
        registry.Register("TestKey", typeof(TestEndpointOptions));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => registry.Register("TestKey", typeof(AnotherEndpointOptions)));
    }

    [TestMethod]
    public void TryGetType_UnknownKey_ReturnsFalseAndNullType()
    {
        var registry = new EndpointOptionsTypeRegistry();
        var found = registry.TryGetType("NonExistentKey", out var type);
        Assert.IsFalse(found);
        Assert.IsNull(type);
    }

    [TestMethod]
    public void TryGetType_IsCaseInsensitive()
    {
        var registry = new EndpointOptionsTypeRegistry();
        registry.Register("AzureDevOpsServices", typeof(TestEndpointOptions));

        var foundLower = registry.TryGetType("azuredevopsservices", out _);
        var foundUpper = registry.TryGetType("AZUREDEVOPSSERVICES", out _);
        var foundExact = registry.TryGetType("AzureDevOpsServices", out _);

        Assert.IsTrue(foundLower, "Registry should be case-insensitive (OrdinalIgnoreCase)");
        Assert.IsTrue(foundUpper, "Registry should be case-insensitive (OrdinalIgnoreCase)");
        Assert.IsTrue(foundExact, "Exact-case lookup should succeed");
    }

    // Stub types for test purposes
    private sealed class TestEndpointOptions : MigrationEndpointOptions
    {
        public override OrganisationEndpoint ToOrganisationEndpoint() => new() { Type = Type };
    }
    private sealed class AnotherEndpointOptions : MigrationEndpointOptions
    {
        public override OrganisationEndpoint ToOrganisationEndpoint() => new() { Type = Type };
    }
}
