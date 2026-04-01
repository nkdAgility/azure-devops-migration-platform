using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Validation;

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

    private readonly IArtefactStore _store;

    public PackageValidator(IArtefactStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        var errors = new List<ValidationError>();

        errors.AddRange(await ValidateManifestAsync(cancellationToken).ConfigureAwait(false));

        await foreach (var path in _store.EnumerateAsync("WorkItems/", cancellationToken))
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
        var raw = await _store.ReadAsync("manifest.json", cancellationToken).ConfigureAwait(false);

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
        var raw = await _store.ReadAsync(path, cancellationToken).ConfigureAwait(false);
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
}
