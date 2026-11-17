// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Builds a job's source and target endpoints from its <c>migration-config.json</c>, with resolved
/// credentials. The one place both agents (net10 and net481) take the job's endpoints from (#169),
/// so the TFS agent's source endpoint carries its token like the MigrationAgent's does.
/// </summary>
public static class ConfiguredJobEndpoints
{
    /// <summary>The job's <c>MigrationPlatform:Source</c> endpoint; false when no source type is set.</summary>
    public static bool TryBuildSource(IConfiguration packageConfig, out ISourceEndpointInfo endpoint)
    {
        var url = ConfigTokenResolver.Resolve(packageConfig["MigrationPlatform:Source:Url"])?.Trim();
        var project = packageConfig["MigrationPlatform:Source:Project"]?.Trim();
        if (string.IsNullOrWhiteSpace(project))
        {
            project = packageConfig
                .GetSection("MigrationPlatform:Source:Generator:Projects")
                .GetChildren()
                .Select(projectSection => projectSection["Name"]?.Trim())
                .FirstOrDefault(projectName => !string.IsNullOrWhiteSpace(projectName));
        }
        var connectorType = packageConfig["MigrationPlatform:Source:Type"];

        if (string.IsNullOrWhiteSpace(connectorType))
        {
            endpoint = null!;
            return false;
        }

        var authSection = packageConfig.GetSection("MigrationPlatform:Source:Authentication");
        endpoint = new ConfiguredSourceEndpoint(
            url ?? string.Empty,
            project ?? string.Empty,
            connectorType!,
            packageConfig["MigrationPlatform:Source:ApiVersion"],
            ParseAuthenticationType(authSection, "MigrationPlatform:Source:Authentication"),
            ConfigTokenResolver.Resolve(authSection?["AccessToken"]));
        return true;
    }

    /// <summary>The job's <c>MigrationPlatform:Target</c> endpoint; false when no target type is set.</summary>
    public static bool TryBuildTarget(IConfiguration packageConfig, out ITargetEndpointInfo endpoint)
    {
        var url = ConfigTokenResolver.Resolve(packageConfig["MigrationPlatform:Target:Url"])?.Trim();
        var project = packageConfig["MigrationPlatform:Target:Project"]?.Trim();
        var connectorType = packageConfig["MigrationPlatform:Target:Type"];

        if (string.IsNullOrWhiteSpace(connectorType))
        {
            endpoint = null!;
            return false;
        }

        var authSection = packageConfig.GetSection("MigrationPlatform:Target:Authentication");
        endpoint = new ConfiguredTargetEndpoint(
            url ?? string.Empty,
            project ?? string.Empty,
            connectorType!,
            packageConfig["MigrationPlatform:Target:ApiVersion"],
            ParseAuthenticationType(authSection, "MigrationPlatform:Target:Authentication"),
            ConfigTokenResolver.Resolve(authSection?["AccessToken"]));
        return true;
    }

    private static AuthenticationType ParseAuthenticationType(IConfiguration authSection, string path)
    {
        var raw = authSection?["Type"];
        if (string.IsNullOrWhiteSpace(raw))
            return AuthenticationType.None;

        if (string.Equals(raw, "Pat", StringComparison.OrdinalIgnoreCase))
            return AuthenticationType.AccessToken;

        if (Enum.TryParse<AuthenticationType>(raw, ignoreCase: true, out var parsed))
            return parsed;

        throw new InvalidOperationException(
            $"Unsupported authentication type '{raw}' at '{path}:Type'. " +
            "Supported values are None, AccessToken, and Windows.");
    }

    private static OrganisationEndpoint ToEndpoint(
        string url, string connectorType, string? apiVersion, AuthenticationType authenticationType, string? accessToken) => new()
    {
        ResolvedUrl = url,
        Type = connectorType,
        ApiVersion = apiVersion,
        Authentication = new OrganisationEndpointAuthentication
        {
            Type = authenticationType,
            ResolvedAccessToken = accessToken
        }
    };

    private sealed record ConfiguredSourceEndpoint(
        string Url,
        string Project,
        string ConnectorType,
        string? ApiVersion,
        AuthenticationType AuthenticationType,
        string? AccessToken) : ISourceEndpointInfo
    {
        public string OrganisationSlug => OrganisationEndpointSlug.ExtractSlug(Url);

        public OrganisationEndpoint ToOrganisationEndpoint() => ToEndpoint(Url, ConnectorType, ApiVersion, AuthenticationType, AccessToken);
    }

    private sealed record ConfiguredTargetEndpoint(
        string Url,
        string Project,
        string ConnectorType,
        string? ApiVersion,
        AuthenticationType AuthenticationType,
        string? AccessToken) : ITargetEndpointInfo
    {
        public string OrganisationSlug => OrganisationEndpointSlug.ExtractSlug(Url);

        public OrganisationEndpoint ToOrganisationEndpoint() => ToEndpoint(Url, ConnectorType, ApiVersion, AuthenticationType, AccessToken);
    }
}
