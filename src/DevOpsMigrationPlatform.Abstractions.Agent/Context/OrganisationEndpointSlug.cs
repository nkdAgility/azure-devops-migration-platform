// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Derives a filesystem-safe organisation slug from an endpoint URL.
/// Used as a route segment in package content paths.
/// </summary>
public static class OrganisationEndpointSlug
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
