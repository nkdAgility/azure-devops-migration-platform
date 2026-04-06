using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Encapsulates environment-specific configuration for system tests
/// </summary>
public class SystemTestConfiguration
{
    private const string OrgEnvVar = "AZDEVOPS_SYSTEM_TEST_ORG";
    private const string TokenEnvVar = "AZDEVOPS_SYSTEM_TEST_PAT";

    /// <summary>
    /// Azure DevOps organization URL from AZDEVOPS_SYSTEM_TEST_ORG
    /// This should be the complete organization URL (e.g., "https://dev.azure.com/nkdagility-preview")
    /// </summary>
    public string? OrganizationUrl { get; private set; }

    /// <summary>
    /// Personal Access Token from AZDEVOPS_SYSTEM_TEST_PAT
    /// </summary>
    public string? AccessToken { get; private set; }

    /// <summary>
    /// Whether all required environment variables are present and valid
    /// </summary>
    public bool IsConfigured => ValidationErrors.Count == 0 && 
                                !string.IsNullOrEmpty(OrganizationUrl) && 
                                !string.IsNullOrEmpty(AccessToken);

    /// <summary>
    /// Configuration validation error messages
    /// </summary>
    public List<string> ValidationErrors { get; } = new List<string>();

    /// <summary>
    /// Creates a new SystemTestConfiguration instance and loads from environment variables
    /// </summary>
    /// <returns>Configuration instance with validation results</returns>
    public static SystemTestConfiguration LoadFromEnvironment()
    {
        var config = new SystemTestConfiguration();
        config.LoadEnvironmentVariables();
        return config;
    }

    private void LoadEnvironmentVariables()
    {
        // Load organization URL
        var orgUrl = Environment.GetEnvironmentVariable(OrgEnvVar);
        if (string.IsNullOrWhiteSpace(orgUrl))
        {
            ValidationErrors.Add($"Environment variable '{OrgEnvVar}' is not set or is empty.");
        }
        else
        {
            OrganizationUrl = orgUrl.Trim();
        }

        // Load access token
        var token = Environment.GetEnvironmentVariable(TokenEnvVar);
        if (string.IsNullOrWhiteSpace(token))
        {
            ValidationErrors.Add($"Environment variable '{TokenEnvVar}' is not set or is empty.");
        }
        else
        {
            AccessToken = token.Trim();
        }
    }

    /// <summary>
    /// Gets a user-friendly configuration error message for test output
    /// </summary>
    public string GetConfigurationErrorMessage()
    {
        if (IsConfigured)
            return string.Empty;

        return "System test skipped: Environment variables not configured. " +
               $"Set {OrgEnvVar} and {TokenEnvVar} to run this test. " +
               "See docs/contributors.md for setup instructions.";
    }
}