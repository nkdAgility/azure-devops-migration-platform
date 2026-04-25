using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Jobs;
#if !NET481
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Resolved configuration for the WorkItems module, derived from the module's
/// <see cref="JobModule.Extensions"/> list and <see cref="JobModule.Query"/>.
///
/// Use <see cref="FromModule"/> to construct an instance from the job contract.
/// Each named extension ("Revisions", "Links", "Attachments", "Comments", "EmbeddedImages")
/// is independently enabled/disabled. Unknown extension types are silently ignored.
/// Missing extensions fall back to enabled defaults.
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
    public CommentsExtensionOptionsConfig Comments { get; init; } = new CommentsExtensionOptionsConfig();

    /// <summary>EmbeddedImages extension options. Default: enabled, 30 s timeout.</summary>
    public EmbeddedImagesExtensionOptionsConfig EmbeddedImages { get; init; } = new EmbeddedImagesExtensionOptionsConfig();

    /// <summary>
    /// Resolution strategy options. A <c>WorkItemResolutionStrategy</c> extension with a valid
    /// <c>strategy</c> value (<c>"TargetField"</c> or <c>"TargetHyperlink"</c>) is required for
    /// import jobs. The factory will throw if the strategy is absent or unrecognised.
    /// </summary>
    public WorkItemResolutionStrategyOptions ResolutionStrategy { get; init; } = new();

    /// <summary>
    /// Work item filters that a work item must satisfy to be included.
    /// Parsed from <c>filter</c> scopes with <c>mode == "include"</c>.
    /// All filters are applied as AND conditions.
    /// Empty when no include filter scopes are configured.
    /// </summary>
    public IReadOnlyList<Models.WorkItemFieldFilterOptions> IncludeFilters { get; init; }
        = Array.Empty<Models.WorkItemFieldFilterOptions>();

    /// <summary>
    /// Work item filters that, if matched, cause a work item to be excluded.
    /// Parsed from <c>filter</c> scopes with <c>mode == "exclude"</c>.
    /// All filters are applied as AND conditions — a work item is excluded only if it matches all exclude filters.
    /// Empty when no exclude filter scopes are configured.
    /// </summary>
    public IReadOnlyList<Models.WorkItemFieldFilterOptions> ExcludeFilters { get; init; }
        = Array.Empty<Models.WorkItemFieldFilterOptions>();

    /// <summary>
    /// Constructs a <see cref="WorkItemsModuleExtensions"/> from a <see cref="JobModule"/>.
    /// Reads the WIQL query from the first <c>"wiql"</c> scope in
    /// <see cref="JobModule.Scopes"/> and iterates
    /// <see cref="JobModule.Extensions"/> by <see cref="JobModuleExtension.Type"/>
    /// to populate sub-module settings. Unknown extension types are silently ignored.
    /// Missing extensions fall back to enabled defaults.
    /// <para>
    /// Also parses <c>"filter"</c> scopes from <see cref="JobModule.Scopes"/> into
    /// <see cref="IncludeFilters"/> and <see cref="ExcludeFilters"/>. Each filter scope is
    /// validated (mode, field, pattern); an <see cref="InvalidOperationException"/> is thrown
    /// on invalid configuration before any work begins.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a filter scope has an unsupported mode, an empty field name,
    /// or an invalid .NET regex pattern.
    /// </exception>
    public static WorkItemsModuleExtensions FromModule(JobModule module)
    {
        var query = GetWiqlQuery(module.Scopes);
        var (includeFilters, excludeFilters) = ParseFilterScopes(module.Scopes);

        bool revisionsEnabled = true;
        bool linksEnabled = true;
        bool attachmentsEnabled = true;
        var comments = new CommentsExtensionOptionsConfig();
        var embeddedImages = new EmbeddedImagesExtensionOptionsConfig();
        var resolutionStrategy = new WorkItemResolutionStrategyOptions();

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
                case "WorkItemResolutionStrategy":
                    resolutionStrategy = ParseResolutionStrategyExtension(ext);
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
            ResolutionStrategy = resolutionStrategy,
            IncludeFilters = includeFilters,
            ExcludeFilters = excludeFilters,
        };
    }

    private static CommentsExtensionOptionsConfig ParseCommentsExtension(JobModuleExtension ext)
    {
        return new CommentsExtensionOptionsConfig
        {
            Enabled = ext.Enabled,
            IncludeDeleted = GetBool(ext.Parameters, "includeDeleted", false),
        };
    }

    private static EmbeddedImagesExtensionOptionsConfig ParseEmbeddedImagesExtension(JobModuleExtension ext)
    {
        return new EmbeddedImagesExtensionOptionsConfig
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

    private static WorkItemResolutionStrategyOptions ParseResolutionStrategyExtension(
        JobModuleExtension ext)
    {
        return new WorkItemResolutionStrategyOptions
        {
            Strategy = GetString(ext.Parameters, "strategy", string.Empty),
            FieldName = GetString(ext.Parameters, "fieldName", string.Empty),
            UrlPattern = GetString(ext.Parameters, "urlPattern", string.Empty),
        };
    }

    private static string GetString(Dictionary<string, object?> p, string key, string defaultValue)
    {
        if (!p.TryGetValue(key, out var raw) || raw is null) return defaultValue;
        if (raw is string s) return s;
        if (raw is JsonElement el && el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? defaultValue;
        var str = raw.ToString();
        return string.IsNullOrEmpty(str) ? defaultValue : str;
    }

    /// <summary>
    /// Extracts the WIQL query string from the first scope with <c>Type == "wiql"</c>.
    /// Falls back to <see cref="DefaultWiqlQuery"/> when no wiql scope is present
    /// or the <c>query</c> parameter is absent/empty.
    /// </summary>
    private static string GetWiqlQuery(System.Collections.Generic.List<JobModuleScope> scopes)
    {
        var wiqlScope = scopes.FirstOrDefault(s =>
            string.Equals(s.Type, "wiql", StringComparison.OrdinalIgnoreCase));

        if (wiqlScope is null) return DefaultWiqlQuery;

        if (!wiqlScope.Parameters.TryGetValue("query", out var raw) || raw is null)
            return DefaultWiqlQuery;

        var q = raw.ToString();
        return string.IsNullOrWhiteSpace(q) ? DefaultWiqlQuery : q;
    }

    /// <summary>
    /// Parses all <c>filter</c> scopes from <paramref name="scopes"/> into validated
    /// <see cref="Models.WorkItemFieldFilterOptions"/> lists, separated by include/exclude mode.
    /// <para>
    /// <strong>Design note:</strong> This method is intentionally shared between export and import
    /// via <see cref="WorkItemsModuleExtensions"/>. Both operations must apply identical filter
    /// semantics so that an import only processes the same subset of work items the export produced.
    /// Divergence between export and import filter parsing would be a correctness bug.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a filter scope has an unsupported mode, an empty field name,
    /// or an invalid .NET regex pattern.
    /// </exception>
    private static (IReadOnlyList<Models.WorkItemFieldFilterOptions> IncludeFilters,
                    IReadOnlyList<Models.WorkItemFieldFilterOptions> ExcludeFilters)
        ParseFilterScopes(System.Collections.Generic.List<JobModuleScope> scopes)
    {
        var includeFilters = new List<Models.WorkItemFieldFilterOptions>();
        var excludeFilters = new List<Models.WorkItemFieldFilterOptions>();

        foreach (var scope in scopes)
        {
            if (!string.Equals(scope.Type, "filter", StringComparison.OrdinalIgnoreCase))
                continue;

            var mode = GetString(scope.Parameters, "mode", string.Empty).Trim();
            var field = GetString(scope.Parameters, "field", string.Empty).Trim();
            var pattern = GetString(scope.Parameters, "pattern", string.Empty).Trim();

            if (!string.Equals(mode, "include", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "exclude", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Filter scope has unsupported mode '{mode}'. Supported values: 'include', 'exclude'.");

            if (string.IsNullOrEmpty(field))
                throw new InvalidOperationException(
                    "Filter scope has an empty or missing 'field' parameter.");

            // Validate the regex by constructing it — fast-fail with a clear error.
            try { _ = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"Filter scope for field '{field}' has an invalid regex pattern '{pattern}': {ex.Message}", ex);
            }

            var filterOptions = new Models.WorkItemFieldFilterOptions(
                field,
                string.Equals(mode, "include", StringComparison.OrdinalIgnoreCase)
                    ? FilterOperator.Regex
                    : FilterOperator.NotRegex,
                pattern);

            if (string.Equals(mode, "include", StringComparison.OrdinalIgnoreCase))
                includeFilters.Add(filterOptions);
            else
                excludeFilters.Add(filterOptions);
        }

        return (includeFilters, excludeFilters);
    }
}
#endif
