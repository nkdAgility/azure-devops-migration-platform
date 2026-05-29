// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;

namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>
/// Resolves the effective package location from a job config payload.
/// </summary>
public static class JobPackageUriResolver
{
    private static readonly string[] s_packageLocationKeys =
    [
        "PackageUri",
        "Uri",
        "WorkingDirectory",
        "Path",
        "Location"
    ];

    /// <summary>
    /// Resolves package location from a job config payload and throws when required values are missing.
    /// </summary>
    public static string ResolveFromConfigPayload(string? configPayload)
    {
        if (!TryResolveFromConfigPayload(configPayload, out var packageUri))
            throw new InvalidOperationException("Job config payload is missing MigrationPlatform.Package location.");

        return packageUri!;
    }

    /// <summary>
    /// Attempts to resolve package location from a job config payload.
    /// </summary>
    public static bool TryResolveFromConfigPayload(string? configPayload, out string? packageUri)
    {
        packageUri = null;
        if (string.IsNullOrWhiteSpace(configPayload))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(configPayload!);
            var root = doc.RootElement;
            if (root.TryGetProperty("MigrationPlatform", out var mp) && mp.ValueKind == JsonValueKind.Object)
                root = mp;

            if (!root.TryGetProperty("Package", out var package) || package.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var key in s_packageLocationKeys)
            {
                if (package.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var resolved = Environment.ExpandEnvironmentVariables(value.GetString() ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        packageUri = resolved;
                        return true;
                    }
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
