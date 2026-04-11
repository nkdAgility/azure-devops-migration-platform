#if !NET481
using System.Collections.Generic;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Strongly-typed scope parameters for the WorkItems module's "wiql" scope type.
/// Deserialized from <see cref="DevOpsMigrationPlatform.Abstractions.Options.MigrationOptionsScope.Parameters"/>
/// at execution time via <see cref="FromParameters"/>.
/// </summary>
public sealed class WorkItemsScopeParameters : IModuleOptions
{
    public const string DefaultWiqlQuery =
        "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";

    /// <inheritdoc/>
    public bool Enabled { get; init; } = true;

    /// <summary>WIQL query selecting work items to export.</summary>
    public string Query { get; init; } = DefaultWiqlQuery;

    /// <summary>Whether to include all revisions (history). Default: <c>true</c>.</summary>
    public bool IncludeRevisions { get; init; } = true;

    /// <summary>Whether to include work item links. Default: <c>true</c>.</summary>
    public bool IncludeLinks { get; init; } = true;

    /// <summary>Whether to include attachment binaries. Default: <c>true</c>.</summary>
    public bool IncludeAttachments { get; init; } = true;

    /// <summary>Configuration for work item comments export. Default: enabled with deleted comments excluded.</summary>
    public CommentsScope Comments { get; init; } = new CommentsScope();

    /// <summary>Configuration for embedded images download and rewriting. Default: enabled with 30s timeout.</summary>
    public EmbeddedImagesScope EmbeddedImages { get; init; } = new EmbeddedImagesScope();

    /// <summary>
    /// Creates an instance from the raw <see cref="Dictionary{TKey,TValue}"/> of
    /// <see cref="JsonElement"/> values stored in
    /// <see cref="DevOpsMigrationPlatform.Abstractions.Options.MigrationOptionsScope.Parameters"/>.
    /// Unknown keys are silently ignored. Missing keys fall back to defaults.
    /// </summary>
    public static WorkItemsScopeParameters FromParameters(Dictionary<string, JsonElement> parameters)
    {
        return new WorkItemsScopeParameters
        {
            Query = GetString(parameters, "query", DefaultWiqlQuery),
            IncludeRevisions = GetBool(parameters, "includeRevisions", true),
            IncludeLinks = GetBool(parameters, "includeLinks", true),
            IncludeAttachments = GetBool(parameters, "includeAttachments", true),
            Comments = ParseCommentsScope(parameters),
            EmbeddedImages = ParseEmbeddedImagesScope(parameters),
        };
    }

    private static CommentsScope ParseCommentsScope(Dictionary<string, JsonElement> parameters)
    {
        if (!parameters.TryGetValue("comments", out var el) || el.ValueKind != JsonValueKind.Object)
            return new CommentsScope();

        return new CommentsScope
        {
            Enabled = GetBoolFromObject(el, "enabled", true),
            IncludeDeleted = GetBoolFromObject(el, "includeDeleted", false),
        };
    }

    private static EmbeddedImagesScope ParseEmbeddedImagesScope(Dictionary<string, JsonElement> parameters)
    {
        if (!parameters.TryGetValue("embeddedImages", out var el) || el.ValueKind != JsonValueKind.Object)
            return new EmbeddedImagesScope();

        return new EmbeddedImagesScope
        {
            Enabled = GetBoolFromObject(el, "enabled", true),
            DownloadTimeoutSeconds = GetIntFromObject(el, "downloadTimeoutSeconds", 30),
        };
    }

    private static bool GetBoolFromObject(JsonElement obj, string key, bool defaultValue)
    {
        if (!obj.TryGetProperty(key, out var el)) return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static int GetIntFromObject(JsonElement obj, string key, int defaultValue)
    {
        if (!obj.TryGetProperty(key, out var el)) return defaultValue;
        return el.TryGetInt32(out var value) ? value : defaultValue;
    }

    private static string GetString(Dictionary<string, JsonElement> p, string key, string defaultValue)
    {
        if (p.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? defaultValue;
        return defaultValue;
    }

    private static bool GetBool(Dictionary<string, JsonElement> p, string key, bool defaultValue)
    {
        if (!p.TryGetValue(key, out var el)) return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }
}
#endif
