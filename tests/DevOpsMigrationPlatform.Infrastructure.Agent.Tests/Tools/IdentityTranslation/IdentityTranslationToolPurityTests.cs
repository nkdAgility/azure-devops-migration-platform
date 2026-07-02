// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityTranslation;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.IdentityTranslation;

/// <summary>
/// TC-M1 / ADR-0026: <see cref="IIdentityTranslationTool"/> is a pure translation engine.
/// Resolved maps are passed in as data (<see cref="IdentityTranslationMap"/>); package I/O
/// and state ownership moved to the Identities orchestrator. These tests pin the resolution
/// order and the parsing/unresolved computation previously embedded in the stateful tool.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("UnitTests")]
public sealed class IdentityTranslationToolPurityTests
{
    private static IdentityTranslationTool CreateTool(bool enabled = true, string? defaultIdentity = null)
        => new(Options.Create(new IdentityTranslationOptions
        {
            Enabled = enabled,
            DefaultIdentity = defaultIdentity
        }));

    private static IdentityTranslationMap Map(
        Dictionary<string, string>? overrides = null,
        Dictionary<string, string>? prepared = null,
        IEnumerable<string>? allUniqueNames = null)
        => new(
            overrides ?? new Dictionary<string, string>(),
            prepared ?? new Dictionary<string, string>(),
            allUniqueNames ?? System.Array.Empty<string>());

    [TestMethod]
    public void Translate_ExplicitOverride_WinsOverPreparedAndDefault()
    {
        var tool = CreateTool(defaultIdentity: "fallback@x");
        var map = Map(
            overrides: new() { ["a@src"] = "a@override" },
            prepared: new() { ["a@src"] = "a@prepared" });

        Assert.AreEqual("a@override", tool.Translate("a@src", map));
    }

    [TestMethod]
    public void Translate_PreparedMatch_UsedWhenNoOverride()
    {
        var tool = CreateTool(defaultIdentity: "fallback@x");
        var map = Map(prepared: new() { ["a@src"] = "a@prepared" });

        Assert.AreEqual("a@prepared", tool.Translate("a@src", map));
    }

    [TestMethod]
    public void Translate_Unresolved_FallsBackToConfiguredDefault()
    {
        var tool = CreateTool(defaultIdentity: "fallback@x");
        Assert.AreEqual("fallback@x", tool.Translate("nobody@src", Map()));
    }

    [TestMethod]
    public void Translate_Unresolved_NoDefault_PassesSourceThrough()
    {
        var tool = CreateTool();
        Assert.AreEqual("nobody@src", tool.Translate("nobody@src", Map()));
    }

    [TestMethod]
    public void Translate_Disabled_ReturnsSourceUnchanged()
    {
        var tool = CreateTool(enabled: false, defaultIdentity: "fallback@x");
        var map = Map(overrides: new() { ["a@src"] = "a@override" });

        Assert.AreEqual("a@src", tool.Translate("a@src", map));
    }

    [TestMethod]
    public void Translate_LookupsAreCaseInsensitive()
    {
        var tool = CreateTool();
        var map = Map(overrides: new() { ["A@SRC"] = "a@target" });

        Assert.AreEqual("a@target", tool.Translate("a@src", map));
    }

    [TestMethod]
    public void ParseTranslationInputs_ReadsDescriptorsOverridesAndPrepared()
    {
        var tool = CreateTool();
        var descriptors = "{\"uniqueName\":\"a@src\"}\n{\"UniqueName\":\"b@src\"}\nnot-json\n{\"other\":1}\n";
        var mapping = "{\"a@src\":\"a@target\"}";
        var prepared = "{\"b@src\":\"b@prepared\"}";

        var map = tool.ParseTranslationInputs(descriptors, mapping, prepared);

        CollectionAssert.AreEquivalent(new[] { "a@src", "b@src" }, map.AllUniqueNames.ToList());
        Assert.AreEqual("a@target", map.Overrides["a@src"]);
        Assert.AreEqual("b@prepared", map.Prepared["b@src"]);
    }

    [TestMethod]
    public void ParseTranslationInputs_MalformedInputs_AreNonFatal()
    {
        var tool = CreateTool();
        var map = tool.ParseTranslationInputs(null, "{bad json", "also bad");

        Assert.AreEqual(0, map.AllUniqueNames.Count);
        Assert.AreEqual(0, map.Overrides.Count);
        Assert.AreEqual(0, map.Prepared.Count);
    }

    [TestMethod]
    public void ComputeUnresolved_ExcludesOverriddenAndPreparedIdentities()
    {
        var tool = CreateTool();
        var map = Map(
            overrides: new() { ["a@src"] = "a@target" },
            prepared: new() { ["b@src"] = "b@prepared" },
            allUniqueNames: new[] { "a@src", "b@src", "c@src" });

        var unresolved = tool.ComputeUnresolved(map);

        CollectionAssert.AreEqual(new[] { "c@src" }, unresolved.ToList());
    }

    [TestMethod]
    public void Tool_HoldsNoPackageOrOrchestratorDependencies()
    {
        // Purity pin: the tool is constructible from options alone (no IPackageAccess,
        // no IIdentitiesOrchestrator, no ISourceEndpointInfo).
        var tool = CreateTool();
        Assert.IsInstanceOfType<IIdentityTranslationTool>(tool);
    }
}
