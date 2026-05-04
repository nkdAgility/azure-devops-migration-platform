// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

[TestClass]
public class AnalyserOptionsValidationTests
{
    // ── Empty / missing organisations ─────────────────────────────────────────

    [TestMethod]
    public void Validate_DefaultOptions_Throws()
    {
        var opts = new AnalyserOptions();
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => opts.Validate());
        StringAssert.Contains(ex.Message, "empty");
    }

    [TestMethod]
    public void Validate_EmptyOrganisationsList_Throws()
    {
        var opts = new AnalyserOptions { Organisations = new() };
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => opts.Validate());
        StringAssert.Contains(ex.Message, "empty");
    }

    // ── Per-entry validation ──────────────────────────────────────────────────

    [TestMethod]
    public void Validate_EntryMissingType_Throws()
    {
        var opts = new AnalyserOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry { Type = string.Empty, Url = "https://dev.azure.com/org" }
            }
        };
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => opts.Validate());
        StringAssert.Contains(ex.Message, "type");
    }

    [TestMethod]
    public void Validate_EntryMissingUrl_Throws()
    {
        var opts = new AnalyserOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry { Type = "AzureDevOpsServices", Url = string.Empty }
            }
        };
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => opts.Validate());
        StringAssert.Contains(ex.Message, "url");
    }

    [TestMethod]
    public void Validate_PatTypeWithEmptyToken_Throws()
    {
        var opts = new AnalyserOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry
                {
                    Type = "AzureDevOpsServices",
                    Url = "https://dev.azure.com/org",
                    Authentication = new EndpointAuthenticationOptions { Type = AuthenticationType.Pat, AccessToken = "" }
                }
            }
        };
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => opts.Validate());
        StringAssert.Contains(ex.Message, "access token");
    }

    [TestMethod]
    public void Validate_PatTypeWithToken_DoesNotThrow()
    {
        var opts = new AnalyserOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry
                {
                    Type = "AzureDevOpsServices",
                    Url = "https://dev.azure.com/org",
                    Authentication = new EndpointAuthenticationOptions { Type = AuthenticationType.Pat, AccessToken = "abc123" }
                }
            }
        };
        opts.Validate(); // must not throw
    }

    [TestMethod]
    public void Validate_ValidEntry_DoesNotThrow()
    {
        var opts = new AnalyserOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry
                {
                    Type = "AzureDevOpsServices",
                    Url = "https://dev.azure.com/myorg"
                }
            }
        };
        opts.Validate(); // must not throw
    }

    [TestMethod]
    public void Validate_MultipleValidEntries_DoesNotThrow()
    {
        var opts = new AnalyserOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry
                {
                    Type = "AzureDevOpsServices",
                    Url = "https://dev.azure.com/org1"
                },
                new AzureDevOpsOrganisationEntry
                {
                    Type = "TeamFoundationServer",
                    Url = "https://tfs.corp.local/DefaultCollection"
                }
            }
        };
        opts.Validate(); // must not throw
    }

    [TestMethod]
    public void Validate_TfsEntryWithNoAuthBlock_DoesNotThrow()
    {
        // Regression: an entry without an explicit <authentication> block must not
        // trigger PAT validation. The default AuthenticationType.None sentinel
        // ensures AnalyserOptions.Validate skips PAT checks for Windows/anonymous TFS entries.
        var opts = new AnalyserOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry
                {
                    Type = "TeamFoundationServer",
                    Url = "http://tfs:8080/tfs/DefaultCollection"
                    // Authentication deliberately omitted — defaults to AuthenticationType.None
                }
            }
        };
        opts.Validate(); // must not throw
    }
}
