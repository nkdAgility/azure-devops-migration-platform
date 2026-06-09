// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.ConfigFlow;

/// <summary>
/// Simulated connector spy injected by the CLI host during configuration-flow tests.
/// Intercepts the source URL and authentication token forwarded by IOptions&lt;SourceOptions&gt;
/// before any network call is attempted.
/// </summary>
internal sealed class ConfigFlowConnectorSpy
{
    public string? CapturedSourceUrl { get; private set; }
    public string? CapturedAuthToken { get; private set; }

    /// <summary>
    /// Called by the simulated connector when the DI container resolves the source options.
    /// </summary>
    public void Capture(string sourceUrl, string authToken)
    {
        CapturedSourceUrl = sourceUrl;
        CapturedAuthToken = authToken;
    }
}
