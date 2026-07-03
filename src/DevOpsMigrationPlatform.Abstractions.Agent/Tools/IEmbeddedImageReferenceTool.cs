// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Canonical embedded-image reference parsing/rewriting Tool (ADR-0026, TC-H2/TC-L3).
/// Pure, stateless, deterministic, phase-agnostic: the single engine for discovering
/// embedded-image references in work-item text (HTML and Markdown) and rewriting them.
/// Both the export path (source URL → package-relative filename) and the import path
/// (package-relative filename → target URL) consume this seam. All I/O — image download,
/// package persistence, target upload — stays with the calling service/orchestrator.
/// </summary>
public interface IEmbeddedImageReferenceTool
{
    /// <summary>
    /// Parses all embedded-image references from <paramref name="text"/> using the
    /// import-side detection rules: Markdown <c>![alt](url)</c> images (URL up to the
    /// first whitespace) and HTML <c>&lt;img src="…"&gt;</c> elements. Returns URLs in
    /// document order (Markdown matches first, then HTML matches), duplicates included.
    /// </summary>
    IReadOnlyList<string> ParseImageReferences(string text);

    /// <summary>
    /// Rewrites <paramref name="text"/> by applying each <paramref name="urlMap"/> entry
    /// as an ordinal find/replace over the whole text (import-side semantics: every
    /// occurrence of a mapped URL is replaced, wherever it appears).
    /// </summary>
    string RewriteImageUrls(string text, IReadOnlyDictionary<string, string> urlMap);

    /// <summary>
    /// Parses the <c>src</c> of every <c>&lt;img&gt;</c> element in <paramref name="html"/>
    /// using the export-side HTML engine, in document order, duplicates included.
    /// Empty <c>src</c> values are omitted.
    /// </summary>
    IReadOnlyList<string> ParseHtmlImageSources(string html);

    /// <summary>
    /// Rewrites the <c>src</c> attribute of <c>&lt;img&gt;</c> elements whose current
    /// source appears in <paramref name="urlMap"/> using the export-side HTML engine.
    /// When the document contains no <c>&lt;img src&gt;</c> elements the input is returned
    /// unchanged; otherwise the document is re-serialised (export-side semantics).
    /// </summary>
    string RewriteHtmlImageSources(string html, IReadOnlyDictionary<string, string> urlMap);

    /// <summary>
    /// Parses the URL of every Markdown image (<c>![alt](url)</c>, URL greedy to the
    /// closing parenthesis — export-side rule) in <paramref name="markdown"/>, in document
    /// order, duplicates included.
    /// </summary>
    IReadOnlyList<string> ParseMarkdownImageReferences(string markdown);

    /// <summary>
    /// Rewrites Markdown images whose URL appears in <paramref name="urlMap"/>, preserving
    /// the alt text (export-side semantics). Unmapped images are left untouched.
    /// </summary>
    string RewriteMarkdownImageReferences(string markdown, IReadOnlyDictionary<string, string> urlMap);
}
