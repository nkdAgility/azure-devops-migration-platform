#if !NET481
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Resolved configuration for the WorkItems module, derived from the module's
/// <see cref="MigrationJobModule.Extensions"/> list and <see cref="MigrationJobModule.Query"/>.
///
/// Use <see cref="FromModule"/> to construct an instance from the job contract.
/// Each named extension ("Revisions", "Links", "Attachments", "Comments", "EmbeddedImages")
/// is independently enabled/disabled. Unknown extension types are silently ignored.
/// </summary>
public sealed class WorkItemsModuleExtensions
{
    public const string DefaultWiqlQuery =
        "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";

    /// <summary>WIQL query selecting work items to export.</summary>
    public string Query { get; init; } = DefaultWiqlQuery;

    /// <summary>Whether revision history export is enabled. Default: <c>true</c>.</summary>
    public bool RevisionsEnabled { get; init; } = true;

    /// <summary>Whether related-link export is enabled. Default: <c>true</c>.</summary>
    public bool LinksEnabled { get; init; } = true;

    /// <summary>Whether attachment binary download is enabled. Default: <c>true</c>.</summary>
    public bool AttachmentsEnabled { get; init; } = true;

    /// <summary>Comments extension configuration. Default: enabled, no deleted comments.</summary>
    public CommentsExtensionOptions Comments { get; init; } = new CommentsExtensionOptions();

    /// <summary>EmbeddedImages extension options. Default: enabled, 30 s timeout.</summary>
    public EmbeddedImagesExtensionOptions EmbeddedImages { get; init; } = new EmbeddedImagesExtensionOptions();

    /// <summary>
    /// Constructs a <see cref="WorkItemsModuleExtensions"/> from a <see cref="MigrationJobModule"/>.
    /// Reads the WIQL query from the first <c>"wiql"</c> scope in
    /// <see cref="MigrationJobModule.Scopes"/> and iterates
    /// <see cref="MigrationJobModule.Extensions"/> by <see cref="MigrationJobModuleExtension.Type"/>
    /// to populate sub-module settings. Unknown extension types are silently ignored.
    /// Missing extensions fall back to enabled defaults.
    /// </summary>
    public static WorkItemsModuleExtensions FromModule(MigrationJobModule module)
    {
        var query = GetWiqlQuery(module.Scopes);

        bool revisionsEnabled = true;
        bool linksEnabled = true;
        bool attachmentsEnabled = true;
        var comments = new CommentsExtensionOptions();
        var embeddedImages = new EmbeddedImagesExtensionOptions();

        foreach (var ext in module.Extensions)
        {
            switch (ext.Type)
            {
                case "Revisions":
                    revisionsEnabled = ext.Enabled;
                    break;
                case "Links":
                    linksEnabled = ext.Enabled;
                    break;
                case "Attachments":
                    attachmentsEnabled = ext.Enabled;
                    break;
                case "Comments":
                    comments = ParseCommentsExtension(ext);
                    break;
                case "EmbeddedImages":
                    embeddedImages = ParseEmbeddedImagesExtension(ext);
                    break;
            }
        }

        return new WorkItemsModuleExtensions
        {
            Query = query,
            RevisionsEnabled = revisionsEnabled,
            LinksEnabled = linksEnabled,
            AttachmentsEnabled = attachmentsEnabled,
            Comments = comments,
            EmbeddedImages = embeddedImages,
        };
    }

    private static CommentsExtensionOptions ParseCommentsExtension(MigrationJobModuleExtension ext)
    {
        return new CommentsExtensionOptions
        {
            Enabled = ext.Enabled,
            IncludeDeleted = GetBool(ext.Parameters, "includeDeleted", false),
        };
    }

    private static EmbeddedImagesExtensionOptions ParseEmbeddedImagesExtension(MigrationJobModuleExtension ext)
    {
        return new EmbeddedImagesExtensionOptions
        {
            Enabled = ext.Enabled,
            DownloadTimeoutSeconds = GetInt(ext.Parameters, "downloadTimeoutSeconds", 30),
        };
    }

    private static bool GetBool(Dictionary<string, object?> p, string key, bool defaultValue)
    {
        if (!p.TryGetValue(key, out var raw) || raw is null) return defaultValue;
        if (raw is bool b) return b;
        if (raw is JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue
            };
        }
        return bool.TryParse(raw.ToString(), out var parsed) ? parsed : defaultValue;
    }

    private static int GetInt(Dictionary<string, object?> p, string key, int defaultValue)
    {
        if (!p.TryGetValue(key, out var raw) || raw is null) return defaultValue;
        if (raw is int i) return i;
        if (raw is JsonElement el && el.TryGetInt32(out var v)) return v;
        return int.TryParse(raw.ToString(), out var parsed) ? parsed : defaultValue;
    }

    /// <summary>
    /// Extracts the WIQL query string from the first scope with <c>Type == "wiql"</c>.
    /// Falls back to <see cref="DefaultWiqlQuery"/> when no wiql scope is present
    /// or the <c>query</c> parameter is absent/empty.
    /// </summary>
    private static string GetWiqlQuery(System.Collections.Generic.List<MigrationJobModuleScope> scopes)
    {
        var wiqlScope = scopes.FirstOrDefault(s =>
            string.Equals(s.Type, "wiql", StringComparison.OrdinalIgnoreCase));

        if (wiqlScope is null) return DefaultWiqlQuery;

        if (!wiqlScope.Parameters.TryGetValue("query", out var raw) || raw is null)
            return DefaultWiqlQuery;

        var q = raw.ToString();
        return string.IsNullOrWhiteSpace(q) ? DefaultWiqlQuery : q;
    }
}
#endif
