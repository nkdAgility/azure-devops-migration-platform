// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;

public interface IProjectLifecycleNameGenerator
{
    string Generate(string runId, string connectorType, string? namePrefix = null);
}

/// <summary>
/// Generates run-correlated, collision-resistant project names.
/// </summary>
public sealed class ProjectLifecycleNameGenerator : IProjectLifecycleNameGenerator
{
    private const int MaxLength = 64;

    public string Generate(string runId, string connectorType, string? namePrefix = null)
    {
        var safePrefix = Sanitize(string.IsNullOrWhiteSpace(namePrefix) ? "ephemeral" : namePrefix!);
        var safeConnector = Sanitize(string.IsNullOrWhiteSpace(connectorType) ? "unknown" : connectorType);
        var safeRun = Sanitize(string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) : runId);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var randomBytes = new byte[4];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        var entropy = BitConverter.ToString(randomBytes).Replace("-", string.Empty).ToLowerInvariant();
        var candidate = $"{safePrefix}-{safeConnector}-{safeRun}-{timestamp}-{entropy}";

        return candidate.Length <= MaxLength
            ? candidate
            : candidate.Substring(0, MaxLength);
    }

    private static string Sanitize(string value)
    {
        var chars = value
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var text = new string(chars);
        while (text.Contains("--"))
            text = text.Replace("--", "-");

        return text.Trim('-');
    }
}
