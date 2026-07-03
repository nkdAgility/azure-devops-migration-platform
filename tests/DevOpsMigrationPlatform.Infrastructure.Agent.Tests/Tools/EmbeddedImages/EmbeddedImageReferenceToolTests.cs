// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.EmbeddedImages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.EmbeddedImages;

/// <summary>
/// TC-H2 / TC-L3 / ADR-0026: the embedded-image reference parse/rewrite engine is a
/// canonical seam (<see cref="IEmbeddedImageReferenceTool"/>) shared by the export and
/// import paths. These tests pin the engine behaviour that was previously duplicated in
/// EmbeddedImageRewriteTool (import) and EmbeddedImageExportService (export).
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("UnitTests")]
public sealed class EmbeddedImageReferenceToolTests
{
    private static readonly EmbeddedImageReferenceTool Tool = new();

    [TestMethod]
    public void ContractIsCanonical_InterfaceLivesInAbstractionsAgent()
    {
        Assert.AreEqual(
            "DevOpsMigrationPlatform.Abstractions.Agent",
            typeof(IEmbeddedImageReferenceTool).Assembly.GetName().Name,
            "IEmbeddedImageReferenceTool must be a canonical Abstractions.Agent seam (TC-L3).");
        Assert.IsInstanceOfType<IEmbeddedImageReferenceTool>(new EmbeddedImageReferenceTool());
    }

    // ── Import-side detection (pinned from EmbeddedImageRewriteTool) ──────────

    [TestMethod]
    public void ParseImageReferences_FindsMarkdownAndHtmlReferences_InOrder()
    {
        var text = "Intro ![diagram](images/flow.png) and <img src=\"shot.png\" alt=\"x\"> done.";
        var refs = Tool.ParseImageReferences(text);
        CollectionAssert.AreEqual(new[] { "images/flow.png", "shot.png" }, refs.ToList());
    }

    [TestMethod]
    public void ParseImageReferences_MarkdownUrlStopsAtWhitespace_TitleExcluded()
    {
        var refs = Tool.ParseImageReferences("![a](img.png \"title\")");
        CollectionAssert.AreEqual(new[] { "img.png" }, refs.ToList());
    }

    [TestMethod]
    public void ParseImageReferences_HtmlSingleQuotesAndCaseInsensitiveTag_Match()
    {
        var refs = Tool.ParseImageReferences("<IMG SRC='a.png'>");
        CollectionAssert.AreEqual(new[] { "a.png" }, refs.ToList());
    }

    [TestMethod]
    public void ParseImageReferences_NoReferences_ReturnsEmpty()
    {
        Assert.AreEqual(0, Tool.ParseImageReferences("plain text").Count);
    }

    [TestMethod]
    public void RewriteImageUrls_ReplacesEveryOccurrence_Ordinal()
    {
        var text = "see img1.png and <img src=\"img1.png\"> plus IMG1.PNG";
        var rewritten = Tool.RewriteImageUrls(text, new Dictionary<string, string>
        {
            ["img1.png"] = "https://target.example/image.png"
        });
        Assert.AreEqual(
            "see https://target.example/image.png and <img src=\"https://target.example/image.png\"> plus IMG1.PNG",
            rewritten);
    }

    [TestMethod]
    public void RewriteImageUrls_EmptyMap_ReturnsInputUnchanged()
    {
        Assert.AreEqual("hello img.png", Tool.RewriteImageUrls("hello img.png", new Dictionary<string, string>()));
    }

    // ── Export-side HTML engine (pinned from EmbeddedImageExportService) ──────

    [TestMethod]
    public void ParseHtmlImageSources_ReturnsSrcValuesInDocumentOrder()
    {
        var html = "<p><img src=\"https://a/1.png\"/>text<img src=\"https://a/2.png\"/><img src=\"https://a/1.png\"/></p>";
        var sources = Tool.ParseHtmlImageSources(html);
        CollectionAssert.AreEqual(
            new[] { "https://a/1.png", "https://a/2.png", "https://a/1.png" },
            sources.ToList());
    }

    [TestMethod]
    public void ParseHtmlImageSources_NoImages_ReturnsEmpty()
    {
        Assert.AreEqual(0, Tool.ParseHtmlImageSources("<p>no images</p>").Count);
    }

    [TestMethod]
    public void RewriteHtmlImageSources_RewritesMappedSources_KeepsUnmapped()
    {
        var html = "<p><img src=\"https://a/1.png\"><img src=\"https://a/2.png\"></p>";
        var rewritten = Tool.RewriteHtmlImageSources(html, new Dictionary<string, string>
        {
            ["https://a/1.png"] = "image-abc.png"
        });
        StringAssert.Contains(rewritten, "src=\"image-abc.png\"");
        StringAssert.Contains(rewritten, "src=\"https://a/2.png\"");
    }

    [TestMethod]
    public void RewriteHtmlImageSources_NoImages_ReturnsInputByteForByte()
    {
        var html = "<p>no images  <b>here</b></p>";
        Assert.AreSame(html, Tool.RewriteHtmlImageSources(html, new Dictionary<string, string> { ["x"] = "y" }));
    }

    // ── Export-side Markdown engine (pinned from EmbeddedImageExportService) ──

    [TestMethod]
    public void ParseMarkdownImageReferences_UrlGreedyToClosingParen()
    {
        var refs = Tool.ParseMarkdownImageReferences("![alt](https://a/1.png) and ![x](img.png \"t\")");
        CollectionAssert.AreEqual(new[] { "https://a/1.png", "img.png \"t\"" }, refs.ToList());
    }

    [TestMethod]
    public void RewriteMarkdownImageReferences_PreservesAltText_RewritesMappedOnly()
    {
        var markdown = "before ![diagram](https://a/1.png) mid ![two](https://a/2.png) after";
        var rewritten = Tool.RewriteMarkdownImageReferences(markdown, new Dictionary<string, string>
        {
            ["https://a/1.png"] = "image-abc.png"
        });
        Assert.AreEqual("before ![diagram](image-abc.png) mid ![two](https://a/2.png) after", rewritten);
    }

    [TestMethod]
    public void RewriteMarkdownImageReferences_DuplicateReferences_AllRewritten()
    {
        var markdown = "![a](u.png) ![b](u.png)";
        var rewritten = Tool.RewriteMarkdownImageReferences(markdown, new Dictionary<string, string>
        {
            ["u.png"] = "local.png"
        });
        Assert.AreEqual("![a](local.png) ![b](local.png)", rewritten);
    }
}
