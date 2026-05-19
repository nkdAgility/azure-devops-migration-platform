// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Validates NodeTranslation configuration against the package contents.
/// Reads <c>Nodes/referenced-paths.json</c> and checks each path against the configured mapping rules.
/// </summary>
public sealed class NodeTranslationValidator : INodeTranslationValidator
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly NodeTranslationOptions _options;
    private readonly INodeTranslationTool _tool;

    public NodeTranslationValidator(IOptions<NodeTranslationOptions> options, INodeTranslationTool tool)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
    }

    /// <inheritdoc/>
    public async Task<NodeTranslationValidationReport> ValidateAsync(
        IPackageAccess package,
        ProjectMapping context,
        CancellationToken ct)
    {
        var malformed = new List<string>();

        // Validate regex patterns in configuration
        ValidateRegexPatterns(_options.AreaPathMappings, malformed);
        ValidateRegexPatterns(_options.IterationPathMappings, malformed);

        // If regex patterns are malformed, we cannot safely run path translation
        if (malformed.Count > 0)
        {
            return new NodeTranslationValidationReport(
                IsValid: false,
                UnmappedPaths: [],
                UnanchoredPaths: [],
                MalformedTargetPaths: malformed);
        }

        var payload = await package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress("Nodes/referenced-paths.json")),
            ct).ConfigureAwait(false);
        if (payload is null)
        {
            return new NodeTranslationValidationReport(
                IsValid: true,
                UnmappedPaths: [],
                UnanchoredPaths: [],
                MalformedTargetPaths: []);
        }

        using var reader = new System.IO.StreamReader(payload.Content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        if (json is null)
        {
            return new NodeTranslationValidationReport(
                IsValid: true,
                UnmappedPaths: [],
                UnanchoredPaths: [],
                MalformedTargetPaths: []);
        }

        var artifact = JsonSerializer.Deserialize<ReferencedPathsArtifact>(json, s_jsonOptions);
        if (artifact is null)
        {
            return new NodeTranslationValidationReport(
                IsValid: true,
                UnmappedPaths: [],
                UnanchoredPaths: [],
                MalformedTargetPaths: []);
        }

        var unmapped = new List<UnmappedPathFinding>();
        var unanchored = new List<UnmappedPathFinding>();

        CheckPaths("System.AreaPath", artifact.AreaPaths, context, unmapped, unanchored);
        CheckPaths("System.IterationPath", artifact.IterationPaths, context, unmapped, unanchored);

        bool isValid = unmapped.Count == 0 && unanchored.Count == 0 && malformed.Count == 0;

        return new NodeTranslationValidationReport(
            IsValid: isValid,
            UnmappedPaths: unmapped,
            UnanchoredPaths: unanchored,
            MalformedTargetPaths: malformed);
    }

    private void CheckPaths(
        string fieldName,
        IReadOnlyList<string> paths,
        ProjectMapping context,
        List<UnmappedPathFinding> unmapped,
        List<UnmappedPathFinding> unanchored)
    {
        foreach (var path in paths)
        {
            var translation = _tool.TranslatePath(fieldName, path, context);

            if (translation.IsExternalPath)
            {
                unanchored.Add(new UnmappedPathFinding(fieldName, path, 1));
            }
            else if (!translation.MatchedByMap && !translation.MatchedByProjectSwap)
            {
                unmapped.Add(new UnmappedPathFinding(fieldName, path, 1));
            }
        }
    }

    private static void ValidateRegexPatterns(IReadOnlyList<NodeMapping> mappings, List<string> malformed)
    {
        foreach (var mapping in mappings)
        {
            try
            {
                _ = new Regex(mapping.Match, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException)
            {
                malformed.Add(mapping.Match);
            }
        }
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}
