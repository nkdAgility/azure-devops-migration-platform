// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Resolved source endpoint values for the current job.
/// Registered by the connector's own Add*Services() extension method.
/// </summary>
public interface ISourceEndpointInfo
{
    /// <summary>Collection URL (e.g. https://dev.azure.com/myorg or http://server/tfs).</summary>
    string Url { get; }

    /// <summary>Source project name or GUID.</summary>
    string Project { get; }

    /// <summary>
    /// Connector type identifier: "AzureDevOpsServices" | "TeamFoundationServer" | "Simulated".
    /// </summary>
    string ConnectorType { get; }

    /// <summary>
    /// Filesystem-safe organisation identifier derived from <see cref="Url"/>.
    /// Used as a route segment in package content paths.
    /// Extracts the last non-empty path segment (e.g. "nkdagility" from
    /// "https://dev.azure.com/nkdagility", "DefaultCollection" from
    /// "http://tfsserver:8080/tfs/DefaultCollection"), or the hostname when no path exists.
    /// </summary>
#if !NET481
    string OrganisationSlug => EndpointSlugHelper.ExtractSlug(Url);
#else
    string OrganisationSlug { get; }
#endif

    /// <summary>
    /// Returns the full <see cref="OrganisationEndpoint"/> for this endpoint, including authentication.
    /// Default implementation returns an endpoint with no authentication (backward-compatible).
    /// Override in connector-specific implementations to include auth credentials.
    /// </summary>
#if !NET481
    OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = Url, Type = ConnectorType };
#else
    OrganisationEndpoint ToOrganisationEndpoint();
#endif
}

/// <summary>
/// Shared helper for extracting a filesystem-safe slug from an endpoint URL.
/// </summary>
public static class EndpointSlugHelper
{
    /// <summary>
    /// Extracts the last non-empty path segment from <paramref name="url"/>,
    /// or the hostname if the URL has no path segments.
    /// </summary>
    public static string ExtractSlug(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "unknown";

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
                return segments[segments.Length - 1];
            return uri.Host;
        }

        // Fallback: treat as opaque string, take last segment after '/'
        var lastSlash = url.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < url.Length - 1)
            return url.Substring(lastSlash + 1);

        return url;
    }
}
