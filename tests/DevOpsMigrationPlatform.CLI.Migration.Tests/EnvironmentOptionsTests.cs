// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel.DataAnnotations;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests;

[TestClass]
public class EnvironmentOptionsTests
{
    private static List<ValidationResult> Validate(object instance)
    {
        var context = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, context, results, validateAllProperties: true);
        if (instance is IValidatableObject validatable)
            results.AddRange(validatable.Validate(context));
        return results;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. No Environment section → defaults applied
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Default_WhenNoEnvironmentSection_TypeIsStandalone()
    {
        var opts = new EnvironmentOptions();

        Assert.AreEqual(EnvironmentType.Standalone, opts.Type);
        Assert.AreEqual("http://localhost:5100", opts.ControlPlane.BaseUrl);
        Assert.IsNull(opts.AgentRunner);
    }

    [TestMethod]
    public void Default_WhenNoEnvironmentSection_ValidationPasses()
    {
        var opts = new EnvironmentOptions();
        var errors = Validate(opts);

        Assert.AreEqual(0, errors.Count);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Standalone explicit → no AgentRunner allowed
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Standalone_WithoutAgentRunner_IsValid()
    {
        var opts = new EnvironmentOptions
        {
            Type = EnvironmentType.Standalone,
            ControlPlane = new ControlPlaneEndpointOptions { BaseUrl = "http://localhost:5100" }
        };

        var errors = Validate(opts);
        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void Standalone_WithAgentRunner_FailsValidation()
    {
        var opts = new EnvironmentOptions
        {
            Type = EnvironmentType.Standalone,
            AgentRunner = new AgentRunnerOptions
            {
                Type = "AzureContainerApps",
                SubscriptionId = "sub-id",
                ResourceGroup = "rg",
                EnvironmentName = "env",
                Auth = new AgentRunnerAuthOptions
                {
                    Type = "ServicePrincipal",
                    TenantId = "tenant",
                    ClientId = "client",
                    ClientSecret = "secret"
                }
            }
        };

        var errors = Validate(opts);
        Assert.IsTrue(errors.Any(e => e.ErrorMessage!.Contains("AgentRunner must be null")));
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Hosted without AgentRunner → valid
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Hosted_WithoutAgentRunner_IsValid()
    {
        var opts = new EnvironmentOptions
        {
            Type = EnvironmentType.Hosted,
            ControlPlane = new ControlPlaneEndpointOptions { BaseUrl = "https://controlplane.example.com" }
        };

        var errors = Validate(opts);
        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void Hosted_WithoutBaseUrl_FailsValidation()
    {
        var opts = new EnvironmentOptions
        {
            Type = EnvironmentType.Hosted,
            ControlPlane = new ControlPlaneEndpointOptions { BaseUrl = "" }
        };

        var errors = Validate(opts);
        Assert.IsTrue(errors.Any(e => e.ErrorMessage!.Contains("ControlPlane.BaseUrl must be provided")));
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. Hosted with AgentRunner + Auth → valid
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Hosted_WithFullAgentRunner_IsValid()
    {
        var opts = new EnvironmentOptions
        {
            Type = EnvironmentType.Hosted,
            ControlPlane = new ControlPlaneEndpointOptions { BaseUrl = "https://controlplane.example.com" },
            AgentRunner = new AgentRunnerOptions
            {
                Type = "AzureContainerApps",
                SubscriptionId = "sub-id",
                ResourceGroup = "rg-name",
                EnvironmentName = "aca-env",
                Auth = new AgentRunnerAuthOptions
                {
                    Type = "ServicePrincipal",
                    TenantId = "tenant-id",
                    ClientId = "client-id",
                    ClientSecret = "client-secret"
                }
            }
        };

        var errors = Validate(opts);
        Assert.AreEqual(0, errors.Count);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 5. Hosted with AgentRunner missing Auth → fail
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Hosted_WithAgentRunnerMissingAuth_FailsValidation()
    {
        var opts = new EnvironmentOptions
        {
            Type = EnvironmentType.Hosted,
            ControlPlane = new ControlPlaneEndpointOptions { BaseUrl = "https://controlplane.example.com" },
            AgentRunner = new AgentRunnerOptions
            {
                Type = "AzureContainerApps",
                SubscriptionId = "sub-id",
                ResourceGroup = "rg-name",
                EnvironmentName = "aca-env",
                Auth = null
            }
        };

        var errors = Validate(opts);
        Assert.IsTrue(errors.Any(e => e.ErrorMessage!.Contains("Auth is required")));
    }

    [TestMethod]
    public void Hosted_WithAgentRunnerMissingFields_FailsValidation()
    {
        var opts = new EnvironmentOptions
        {
            Type = EnvironmentType.Hosted,
            ControlPlane = new ControlPlaneEndpointOptions { BaseUrl = "https://controlplane.example.com" },
            AgentRunner = new AgentRunnerOptions
            {
                Type = "",
                SubscriptionId = "",
                ResourceGroup = "",
                EnvironmentName = "",
                Auth = new AgentRunnerAuthOptions
                {
                    Type = "ServicePrincipal",
                    TenantId = "t",
                    ClientId = "c",
                    ClientSecret = "s"
                }
            }
        };

        var errors = Validate(opts);
        Assert.IsTrue(errors.Any(e => e.ErrorMessage!.Contains("Type is required")));
        Assert.IsTrue(errors.Any(e => e.ErrorMessage!.Contains("SubscriptionId is required")));
    }

    [TestMethod]
    public void AgentRunnerAuth_MissingFields_FailsValidation()
    {
        var auth = new AgentRunnerAuthOptions
        {
            Type = "ServicePrincipal",
            TenantId = "",
            ClientId = "",
            ClientSecret = ""
        };

        var errors = Validate(auth);
        Assert.IsTrue(errors.Any(e => e.ErrorMessage!.Contains("TenantId is required")));
        Assert.IsTrue(errors.Any(e => e.ErrorMessage!.Contains("ClientId is required")));
        Assert.IsTrue(errors.Any(e => e.ErrorMessage!.Contains("ClientSecret is required")));
    }
}
