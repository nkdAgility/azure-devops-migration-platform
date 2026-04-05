using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

[TestClass]
public class InventoryOptionsValidationTests
{
    // ── Mutual exclusion ──────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_BothSourceAndOrganisations_Throws()
    {
        var opts = new InventoryOptions
        {
            Source = new MigrationEndpointOptions { Project = "MyProject" },
            Organisations = new() { new OrganisationEntry { Type = "AzureDevOpsServices", OrgOrCollection = "https://dev.azure.com/org" } }
        };
        var ex = Assert.ThrowsException<InvalidOperationException>(() => opts.Validate(false));
        StringAssert.Contains(ex.Message, "mutually exclusive");
    }

    [TestMethod]
    public void Validate_NeitherSourceNorOrganisations_Throws()
    {
        var opts = new InventoryOptions();
        Assert.ThrowsException<InvalidOperationException>(() => opts.Validate(false));
    }

    // ── Mode 1 validation ─────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_Mode1_NoProjectAndNoAllProjects_Throws()
    {
        var opts = new InventoryOptions
        {
            Source = new MigrationEndpointOptions { Project = string.Empty }
        };
        var ex = Assert.ThrowsException<InvalidOperationException>(() => opts.Validate(false));
        StringAssert.Contains(ex.Message, "--all-projects");
    }

    [TestMethod]
    public void Validate_Mode1_NoProjectWithAllProjectsFlag_DoesNotThrow()
    {
        var opts = new InventoryOptions
        {
            Source = new MigrationEndpointOptions { Project = string.Empty }
        };
        opts.Validate(allProjectsFlag: true); // must not throw
    }

    [TestMethod]
    public void Validate_Mode1_WithProjectSet_DoesNotThrow()
    {
        var opts = new InventoryOptions
        {
            Source = new MigrationEndpointOptions { Project = "MyProject" }
        };
        opts.Validate(allProjectsFlag: false); // must not throw
    }

    [TestMethod]
    public void Validate_Mode1_PatTypeWithEmptyToken_Throws()
    {
        var opts = new InventoryOptions
        {
            Source = new MigrationEndpointOptions
            {
                Project = "MyProject",
                Authentication = new EndpointAuthenticationOptions { Type = "Pat", AccessToken = "" }
            }
        };
        var ex = Assert.ThrowsException<InvalidOperationException>(() => opts.Validate(false));
        StringAssert.Contains(ex.Message, "PAT");
    }

    [TestMethod]
    public void Validate_Mode1_PatTypeWithToken_DoesNotThrow()
    {
        var opts = new InventoryOptions
        {
            Source = new MigrationEndpointOptions
            {
                Project = "MyProject",
                Authentication = new EndpointAuthenticationOptions { Type = "Pat", AccessToken = "abc123" }
            }
        };
        opts.Validate(allProjectsFlag: false); // must not throw
    }

    // ── Mode 2 validation ─────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_Mode2_EmptyList_Throws()
    {
        var opts = new InventoryOptions { Organisations = new() };
        var ex = Assert.ThrowsException<InvalidOperationException>(() => opts.Validate(false));
        StringAssert.Contains(ex.Message, "empty");
    }

    [TestMethod]
    public void Validate_Mode2_EntryMissingType_Throws()
    {
        var opts = new InventoryOptions
        {
            Organisations = new()
            {
                new OrganisationEntry { Type = string.Empty, OrgOrCollection = "https://dev.azure.com/org" }
            }
        };
        var ex = Assert.ThrowsException<InvalidOperationException>(() => opts.Validate(false));
        StringAssert.Contains(ex.Message, "type");
    }

    [TestMethod]
    public void Validate_Mode2_EntryMissingOrgOrCollection_Throws()
    {
        var opts = new InventoryOptions
        {
            Organisations = new()
            {
                new OrganisationEntry { Type = "AzureDevOpsServices", OrgOrCollection = string.Empty }
            }
        };
        var ex = Assert.ThrowsException<InvalidOperationException>(() => opts.Validate(false));
        StringAssert.Contains(ex.Message, "orgOrCollection");
    }

    [TestMethod]
    public void Validate_Mode2_ValidEntry_DoesNotThrow()
    {
        var opts = new InventoryOptions
        {
            Organisations = new()
            {
                new OrganisationEntry
                {
                    Type = "AzureDevOpsServices",
                    OrgOrCollection = "https://dev.azure.com/myorg"
                }
            }
        };
        opts.Validate(allProjectsFlag: false); // must not throw
    }
}
