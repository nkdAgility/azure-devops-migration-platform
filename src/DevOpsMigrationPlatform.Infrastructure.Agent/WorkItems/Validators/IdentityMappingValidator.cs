// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.FailurePatterns;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Validators;

internal sealed class IdentityMappingValidator(IIdentityMappingService identityMappingService) : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_UNRESOLVED_IDENTITY";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        var mappingJson = await ReadPackageTextAsync(
            context.PrepareContext.Package,
            "Identities/mapping.json",
            cancellationToken).ConfigureAwait(false);
        identityMappingService.LoadMappingOverrides(mappingJson);
        var explicitMappings = ParseExplicitMappings(mappingJson);

        var descriptorsJson = await ReadPackageTextAsync(
            context.PrepareContext.Package,
            "Identities/descriptors.jsonl",
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(descriptorsJson))
        {
            return [];
        }
        var nonEmptyDescriptorsJson = descriptorsJson!;

        var unresolvedIdentities = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var identity in EnumerateIdentityKeys(nonEmptyDescriptorsJson))
        {
            var resolvedIdentity = identityMappingService.Resolve(identity);
            if (string.IsNullOrWhiteSpace(resolvedIdentity)
                || !explicitMappings.Contains(identity))
            {
                unresolvedIdentities.Add(identity);
            }
        }

        return unresolvedIdentities
            .OrderBy(i => i, System.StringComparer.OrdinalIgnoreCase)
            .Select(identity => new ImportFailureFinding(
                PatternCode,
                ImportFailureSeverity.Warning,
                identity,
                $"No explicit identity mapping was found for '{identity}'.",
                "Add an explicit mapping in Identities/mapping.json or confirm fallback behavior before import."))
            .ToList();
    }

    private static IEnumerable<string> EnumerateIdentityKeys(string descriptorsJson)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var line in descriptorsJson.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            IdentityDescriptor? descriptor;
            try
            {
                descriptor = JsonSerializer.Deserialize<IdentityDescriptor>(line, options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (descriptor is null)
            {
                continue;
            }

            var key = FirstNonEmpty(descriptor.UniqueName, descriptor.Descriptor, descriptor.DisplayName);
            if (!string.IsNullOrWhiteSpace(key))
            {
                yield return key!;
            }
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static HashSet<string> ParseExplicitMappings(string? mappingJson)
    {
        if (string.IsNullOrWhiteSpace(mappingJson))
        {
            return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(mappingJson!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return parsed is null
                ? new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    parsed.Keys.Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()),
                    System.StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task<string?> ReadPackageTextAsync(IPackageAccess package, string relativePath, CancellationToken cancellationToken)
    {
        var payload = await package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(relativePath)),
            cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        if (payload.Content.CanSeek)
        {
            payload.Content.Position = 0;
        }

        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}
