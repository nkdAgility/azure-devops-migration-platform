// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Validation;

/// <summary>
/// Validates a migration package by:
/// 1. Checking manifest.json exists and declares a supported schema version.
/// 2. Checking every revision.json contains the required fields.
/// Read-only — no files are created or modified.
/// </summary>
public class PackageValidator : IPackageValidator
{
    private static readonly string[] SupportedSchemaVersions = { "1.0" };
    private static readonly string[] RequiredRevisionFields =
    {
        "workItemId", "revisionIndex", "changedDate",
        "fields", "externalLinks", "relatedLinks", "hyperlinks", "attachments"
    };

    private readonly IPackageAccess _package;

    public PackageValidator(IPackageAccess package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        errors.AddRange(await ValidateManifestAsync(cancellationToken).ConfigureAwait(false));

        await foreach (var path in EnumeratePackageContentAsync("WorkItems/", cancellationToken))
        {
            if (!path.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)) continue;
            var err = await ValidateRevisionAsync(path, cancellationToken).ConfigureAwait(false);
            if (err != null) errors.Add(err);
        }

        return errors.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(errors);
    }

    private async Task<IReadOnlyList<ValidationError>> ValidateManifestAsync(CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();
        var raw = await ReadPackageTextAsync("manifest.json", cancellationToken).ConfigureAwait(false);

        if (raw == null)
        {
            errors.Add(new ValidationError { Path = "manifest.json", Message = "manifest.json not found." });
            return errors;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("schemaVersion", out var sv))
            {
                errors.Add(new ValidationError { Path = "manifest.json", Message = "Missing 'schemaVersion' field." });
                return errors;
            }

            var version = sv.GetString() ?? string.Empty;
            if (Array.IndexOf(SupportedSchemaVersions, version) < 0)
                errors.Add(new ValidationError
                {
                    Path = "manifest.json",
                    Message = $"Unsupported schema version '{version}'."
                });
        }
        catch (JsonException ex)
        {
            errors.Add(new ValidationError { Path = "manifest.json", Message = $"Invalid JSON: {ex.Message}" });
        }

        return errors;
    }

    private async Task<ValidationError?> ValidateRevisionAsync(string path, CancellationToken cancellationToken)
    {
        var raw = await ReadPackageTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (raw == null)
            return new ValidationError { Path = path, Message = "File not found." };

        try
        {
            using var doc = JsonDocument.Parse(raw);
            foreach (var field in RequiredRevisionFields)
            {
                if (!doc.RootElement.TryGetProperty(field, out _))
                    return new ValidationError { Path = path, Message = $"Missing required field '{field}'." };
            }
        }
        catch (JsonException ex)
        {
            return new ValidationError { Path = path, Message = $"Invalid JSON: {ex.Message}" };
        }

        return null;
    }

    private async IAsyncEnumerable<string> EnumeratePackageContentAsync(
        string relativePath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var paths = _package.EnumerateContentAsync(
            new PackageContentContext(
                PackageContentKind.Collection,
                Address: new RelativePathAddress(relativePath),
                IsCollectionRequest: true),
            cancellationToken);
        if (paths is null)
            yield break;

        await foreach (var path in paths.ConfigureAwait(false))
            yield return path;
    }

    private async Task<string?> ReadPackageTextAsync(string relativePath, CancellationToken cancellationToken)
    {
        var payload = await _package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(relativePath)),
            cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}
