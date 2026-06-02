// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Validation;

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
    private readonly string _organisation;
    private readonly string _project;

    public PackageValidator(IPackageAccess package, string organisation, string project)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _organisation = organisation ?? throw new ArgumentNullException(nameof(organisation));
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        errors.AddRange(await ValidateManifestAsync(cancellationToken).ConfigureAwait(false));

        await foreach (var path in EnumerateWorkItemsAsync(cancellationToken))
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
        var raw = await ReadManifestTextAsync(cancellationToken).ConfigureAwait(false);

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
        var raw = await ReadWorkItemTextAsync(path, cancellationToken).ConfigureAwait(false);
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

    private async IAsyncEnumerable<string> EnumerateWorkItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var paths = _package.EnumerateContentAsync(
            new PackageContentContext(
                PackageContentKind.Collection,
                Organisation: _organisation,
                Project: _project,
                Module: "WorkItems",
                IsCollectionRequest: true),
            cancellationToken);
        if (paths is null)
            yield break;

        await foreach (var path in paths.ConfigureAwait(false))
            yield return path;
    }

    private async Task<string?> ReadManifestTextAsync(CancellationToken cancellationToken)
    {
        var payload = await _package.RequestIndexAsync(
            new PackageIndexContext("manifest.json", Organisation: _organisation, Project: _project),
            cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task<string?> ReadWorkItemTextAsync(string artefactPath, CancellationToken cancellationToken)
    {
        var withinModulePath = StripWorkItemsModulePrefix(artefactPath);
        var payload = await _package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: _organisation,
                Project: _project,
                Module: "WorkItems",
                Address: new RelativePathAddress(withinModulePath)),
            cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Strips the leading "{org}/{project}/WorkItems/" or "WorkItems/" prefix from a full path
    /// returned by <see cref="IPackageAccess.EnumerateContentAsync"/>, returning just the
    /// within-module relative path segment.
    /// </summary>
    private string StripWorkItemsModulePrefix(string artefactPath)
    {
        var normalized = artefactPath.Replace('\\', '/').TrimStart('/');

        var scopedPrefix = $"{_organisation}/{_project}/WorkItems/";
        if (normalized.StartsWith(scopedPrefix, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(scopedPrefix.Length);

        const string barePrefix = "WorkItems/";
        if (normalized.StartsWith(barePrefix, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(barePrefix.Length);

        return normalized;
    }
}
