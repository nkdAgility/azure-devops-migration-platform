using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

/// <summary>
/// Verifies that <see cref="MigrationOptions"/> correctly deserializes the <c>modules</c>
/// array from JSON and that the default (empty list) is applied when the key is absent.
/// </summary>
[TestClass]
public class MigrationOptionsDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void Deserialize_WithoutModulesKey_DefaultsToEmptyList()
    {
        var json = """
            {
              "configVersion": "2.0",
              "mode": "Export",
              "artefacts": { "path": "D:\\exports" }
            }
            """;

        var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts);

        Assert.IsNotNull(opts);
        Assert.IsNotNull(opts.Modules);
        Assert.AreEqual(0, opts.Modules.Count);
    }

    [TestMethod]
    public void Deserialize_WithModulesArray_PopulatesModulesList()
    {
        var json = """
            {
              "configVersion": "2.0",
              "mode": "Export",
              "artefacts": { "path": "D:\\exports" },
              "modules": [
                {
                  "name": "WorkItems",
                  "enabled": true,
                  "scopes": [
                    { "type": "wiql", "parameters": { "query": "SELECT [System.Id] FROM WorkItems" } }
                  ],
                  "extensions": [
                    { "type": "Revisions", "enabled": true }
                  ]
                }
              ]
            }
            """;

        var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts);

        Assert.IsNotNull(opts);
        Assert.AreEqual(1, opts.Modules.Count);
        Assert.AreEqual("WorkItems", opts.Modules[0].Name);
        Assert.IsTrue(opts.Modules[0].Enabled);
        Assert.AreEqual(1, opts.Modules[0].Scopes.Count);
        Assert.AreEqual("wiql", opts.Modules[0].Scopes[0].Type);
        Assert.AreEqual(1, opts.Modules[0].Extensions.Count);
        Assert.AreEqual("Revisions", opts.Modules[0].Extensions[0].Type);
    }

    [TestMethod]
    public void Deserialize_WorkItemsExtensions_AllFivePresent()
    {
        var json = """
            {
              "configVersion": "2.0",
              "mode": "Export",
              "artefacts": { "path": "D:\\exports" },
              "modules": [
                {
                  "name": "WorkItems",
                  "scopes": [
                    { "type": "wiql", "parameters": { "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project" } }
                  ],
                  "extensions": [
                    { "type": "Revisions",      "enabled": true },
                    { "type": "Links",          "enabled": true },
                    { "type": "Attachments",    "enabled": false },
                    { "type": "Comments",       "enabled": true,  "parameters": { "includeDeleted": true } },
                    { "type": "EmbeddedImages", "enabled": true }
                  ]
                }
              ]
            }
            """;

        var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts)!;
        var module = opts.Modules[0];

        Assert.AreEqual(1, module.Scopes.Count);
        Assert.AreEqual("wiql", module.Scopes[0].Type);
        Assert.AreEqual("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project",
            module.Scopes[0].Parameters["query"].GetString());
        Assert.AreEqual(5, module.Extensions.Count);

        Assert.AreEqual("Revisions",      module.Extensions[0].Type);
        Assert.IsTrue(module.Extensions[0].Enabled);

        Assert.AreEqual("Links",          module.Extensions[1].Type);
        Assert.IsTrue(module.Extensions[1].Enabled);

        Assert.AreEqual("Attachments",    module.Extensions[2].Type);
        Assert.IsFalse(module.Extensions[2].Enabled);

        Assert.AreEqual("Comments",       module.Extensions[3].Type);
        Assert.IsTrue(module.Extensions[3].Enabled);
        Assert.AreEqual(JsonValueKind.True, module.Extensions[3].Parameters["includeDeleted"].ValueKind);

        Assert.AreEqual("EmbeddedImages", module.Extensions[4].Type);
        Assert.IsTrue(module.Extensions[4].Enabled);
    }

    [TestMethod]
    public void Deserialize_MultipleModules_AllDeserialized()
    {
        var json = """
            {
              "configVersion": "2.0",
              "mode": "Export",
              "artefacts": { "path": "D:\\exports" },
              "modules": [
                { "name": "WorkItems", "enabled": true,  "extensions": [] },
                { "name": "Pipelines", "enabled": false, "extensions": [] }
              ]
            }
            """;

        var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts)!;

        Assert.AreEqual(2, opts.Modules.Count);
        Assert.AreEqual("WorkItems", opts.Modules[0].Name);
        Assert.IsTrue(opts.Modules[0].Enabled);
        Assert.AreEqual("Pipelines", opts.Modules[1].Name);
        Assert.IsFalse(opts.Modules[1].Enabled);
    }
}
