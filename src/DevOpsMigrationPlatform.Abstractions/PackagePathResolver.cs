// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Resolves filesystem-safe organisation and project folder names from Azure DevOps
/// and TFS endpoint URLs.
/// </summary>
public static class PackagePathResolver
{
    /// <summary>
    /// Extracts a filesystem-safe organisation name from an Azure DevOps or TFS URL.
    /// <list type="bullet">
    ///   <item><c>https://dev.azure.com/contoso</c> → <c>contoso</c></item>
    ///   <item><c>https://contoso.visualstudio.com</c> → <c>contoso</c></item>
    ///   <item><c>http://tfs:8080/tfs/DefaultCollection</c> → <c>tfs</c></item>
    /// </list>
    /// </summary>
    public static string ExtractOrgFolderName(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "unknown";

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0)
                    return Sanitise(segments[0]);
            }

            var hostParts = uri.Host.Split('.');
            if (hostParts.Length >= 3 && hostParts[hostParts.Length - 2].Equals("visualstudio", StringComparison.OrdinalIgnoreCase))
                return Sanitise(hostParts[0]);

            return Sanitise(uri.Host);
        }

        return Sanitise(url);
    }

    /// <summary>
    /// Replaces characters that are unsafe in folder names with underscores.
    /// </summary>
    public static string Sanitise(string name)
    {
        var clean = Regex.Replace(name, @"[^\w\-]", "_");
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }

    /// <summary>
    /// Derives a filesystem-safe slug from an inventory org URL using the last path segment.
    /// Unlike <see cref="ExtractOrgFolderName"/> (which uses the host for TFS),
    /// this preserves the collection name: <c>http://tfs:8080/tfs/DefaultCollection</c> → <c>DefaultCollection</c>.
    /// Used exclusively for inventory file paths inside a discovery package.
    /// </summary>
    public static string DeriveInventoryOrgSlug(string orgUrl)
    {
        if (string.IsNullOrWhiteSpace(orgUrl))
            return "unknown";
        if (!Uri.TryCreate(orgUrl.TrimEnd('/'), UriKind.Absolute, out var uri))
            return Sanitise(orgUrl);
        var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var slug = segments.Length > 0 ? segments[segments.Length - 1] : uri.Host;
        return Sanitise(slug);
    }

    /// <summary>
    /// Returns the per-project inventory path: <c>{orgSlug}/{project}/inventory.json</c>.
    /// </summary>
    public static string ProjectInventoryPath(string orgSlug, string project)
        => $"{orgSlug}/{project}/inventory.json";

    /// <summary>
    /// Returns the per-org inventory path: <c>{orgSlug}/inventory.json</c>.
    /// </summary>
    public static string OrgInventoryPath(string orgSlug)
        => $"{orgSlug}/inventory.json";

    /// <summary>Root inventory path: <c>inventory.json</c>.</summary>
    public const string RootInventoryPath = "inventory.json";
}
