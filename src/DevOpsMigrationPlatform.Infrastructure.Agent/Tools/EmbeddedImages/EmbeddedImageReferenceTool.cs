// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
#if !NET481
using HtmlAgilityPack;
#endif

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.EmbeddedImages;

/// <summary>
/// Canonical <see cref="IEmbeddedImageReferenceTool"/> implementation (ADR-0026, TC-H2/TC-L3).
/// Pure and stateless — the parsing/rewriting engines were extracted verbatim from
/// <c>EmbeddedImageRewriteTool</c> (import path) and <c>EmbeddedImageExportService</c>
/// (export path) so both phases share one reference engine. No I/O, no accumulation.
/// </summary>
public sealed class EmbeddedImageReferenceTool : IEmbeddedImageReferenceTool
{
    // Import-side detection rules (pinned from EmbeddedImageRewriteTool).
    private static readonly Regex ImportMarkdownImageRegex =
        new(@"!\[[^\]]*\]\((?<url>[^)\s]+)[^)]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ImportHtmlImageRegex =
        new(@"<img\b[^>]*\bsrc\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Export-side Markdown rule (pinned from EmbeddedImageExportService): ![alt](url), URL greedy to ')'.
    private static readonly Regex ExportMarkdownImageRegex =
        new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc/>
    public IReadOnlyList<string> ParseImageReferences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var references = new List<string>();
        foreach (Match match in ImportMarkdownImageRegex.Matches(text))
        {
            var url = match.Groups["url"].Value;
            if (!string.IsNullOrWhiteSpace(url))
                references.Add(url);
        }

        foreach (Match match in ImportHtmlImageRegex.Matches(text))
        {
            var url = match.Groups["url"].Value;
            if (!string.IsNullOrWhiteSpace(url))
                references.Add(url);
        }

        return references;
    }

    /// <inheritdoc/>
    public string RewriteImageUrls(string text, IReadOnlyDictionary<string, string> urlMap)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (urlMap is null) throw new ArgumentNullException(nameof(urlMap));

        var rewritten = text;
        foreach (var mapping in urlMap)
            rewritten = rewritten.Replace(mapping.Key, mapping.Value);
        return rewritten;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ParseHtmlImageSources(string html)
    {
        if (string.IsNullOrEmpty(html))
            return Array.Empty<string>();

#if !NET481
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes == null || imgNodes.Count == 0)
            return Array.Empty<string>();

        var sources = new List<string>(imgNodes.Count);
        foreach (var img in imgNodes)
        {
            var src = img.GetAttributeValue("src", string.Empty);
            if (!string.IsNullOrEmpty(src))
                sources.Add(src);
        }
        return sources;
#else
        // net481: HtmlAgilityPack is not referenced; the export path does not exist on this
        // runtime, so the regex engine is a sufficient fallback for reference detection.
        return ImportHtmlImageRegex.Matches(html).Cast<Match>()
            .Select(m => m.Groups["url"].Value)
            .Where(u => !string.IsNullOrEmpty(u))
            .ToList();
#endif
    }

    /// <inheritdoc/>
    public string RewriteHtmlImageSources(string html, IReadOnlyDictionary<string, string> urlMap)
    {
        if (urlMap is null) throw new ArgumentNullException(nameof(urlMap));
        if (string.IsNullOrEmpty(html))
            return html;

#if !NET481
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes == null || imgNodes.Count == 0)
            return html;

        foreach (var img in imgNodes)
        {
            var src = img.GetAttributeValue("src", string.Empty);
            if (!string.IsNullOrEmpty(src) && urlMap.TryGetValue(src, out var replacement))
                img.SetAttributeValue("src", replacement);
        }

        return doc.DocumentNode.OuterHtml;
#else
        // net481 fallback (export path unused on this runtime): ordinal src replacement.
        var result = html;
        foreach (var mapping in urlMap)
            result = result.Replace(mapping.Key, mapping.Value);
        return result;
#endif
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ParseMarkdownImageReferences(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return Array.Empty<string>();

        return ExportMarkdownImageRegex.Matches(markdown).Cast<Match>()
            .Select(m => m.Groups[2].Value)
            .ToList();
    }

    /// <inheritdoc/>
    public string RewriteMarkdownImageReferences(string markdown, IReadOnlyDictionary<string, string> urlMap)
    {
        if (urlMap is null) throw new ArgumentNullException(nameof(urlMap));
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        var matches = ExportMarkdownImageRegex.Matches(markdown).Cast<Match>().ToList();
        var result = markdown;

        // Process matches in reverse to maintain string positions (pinned export behaviour).
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var altText = match.Groups[1].Value;
            var originalUrl = match.Groups[2].Value;
            if (!urlMap.TryGetValue(originalUrl, out var replacementUrl))
                continue;

            var replacement = $"![{altText}]({replacementUrl})";
            result = result.Substring(0, match.Index) + replacement + result.Substring(match.Index + match.Length);
        }

        return result;
    }
}
