// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Controls process-level environment variables for system test scenarios.
/// Restores original values on disposal.
/// </summary>
public sealed class SystemTestEnvironment : IDisposable
{
    private const string OrgEnvVar = "AZDEVOPS_SYSTEM_TEST_ORG";
    private const string PatEnvVar = "AZDEVOPS_SYSTEM_TEST_PAT";

    private readonly string? _previousOrg;
    private readonly string? _previousPat;
    private bool _disposed;

    private SystemTestEnvironment(string? org, string? pat)
    {
        _previousOrg = Environment.GetEnvironmentVariable(OrgEnvVar);
        _previousPat = Environment.GetEnvironmentVariable(PatEnvVar);

        Environment.SetEnvironmentVariable(OrgEnvVar, org);
        Environment.SetEnvironmentVariable(PatEnvVar, pat);

        OrgUrl = org;
        Pat = pat;
        IsConfigured = !string.IsNullOrWhiteSpace(org) && !string.IsNullOrWhiteSpace(pat);
    }

    /// <summary>Azure DevOps organisation URL set for this scope.</summary>
    public string? OrgUrl { get; }

    /// <summary>PAT value set for this scope.</summary>
    public string? Pat { get; }

    /// <summary>True when both ORG and PAT are non-empty.</summary>
    public bool IsConfigured { get; }

    /// <summary>
    /// Scope with valid credentials read from the current environment variables.
    /// Does not change env vars — creates a read-only view of what is already set.
    /// </summary>
    public static SystemTestEnvironment WithValidCredentials(string orgUrl, string pat)
        => new(orgUrl, pat);

    /// <summary>Scope with both variables cleared.</summary>
    public static SystemTestEnvironment WithMissingVariables()
        => new(null, null);

    /// <summary>Scope where the PAT value is intentionally invalid (too short).</summary>
    public static SystemTestEnvironment WithInvalidToken(string orgUrl, string shortToken = "bad")
        => new(orgUrl, shortToken);

    /// <summary>
    /// Calls <see cref="Assert.Fail"/> when the environment is not configured.
    /// A missing prerequisite is a defect in the environment and must fail visibly.
    /// </summary>
    public void FailIfNotConfigured()
    {
        if (!IsConfigured)
        {
            Assert.Fail(
                "System test cannot run: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT " +
                "must both be set. See docs/contributors.md for setup instructions.");
        }
    }

    /// <summary>
    /// Calls <see cref="Assert.Fail"/> when the PAT value fails the length threshold.
    /// A misconfigured token is a defect in the environment and must fail visibly.
    /// </summary>
    public void FailIfInvalidToken()
    {
        if (!string.IsNullOrEmpty(Pat) && Pat.Length < 10)
        {
            Assert.Fail(
                $"System test cannot run: PAT for organisation '{OrgUrl}' is invalid (too short). " +
                "Verify AZDEVOPS_SYSTEM_TEST_PAT token has required permissions. " +
                "See docs/contributors.md troubleshooting section.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Environment.SetEnvironmentVariable(OrgEnvVar, _previousOrg);
        Environment.SetEnvironmentVariable(PatEnvVar, _previousPat);
    }
}
