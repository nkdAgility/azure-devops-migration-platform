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
              "configVersion": "1.0",
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
              "configVersion": "1.0",
              "mode": "Export",
              "artefacts": { "path": "D:\\exports" },
              "modules": [
                {
                  "name": "WorkItems",
                  "enabled": true,
                  "scopes": [
                    { "type": "wiql", "parameters": { "query": "SELECT [System.Id] FROM WorkItems" } }
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
    }

    [TestMethod]
    public void Deserialize_WorkItemsScope_ParametersAccessible()
    {
        var json = """
            {
              "configVersion": "1.0",
              "mode": "Export",
              "artefacts": { "path": "D:\\exports" },
              "modules": [
                {
                  "name": "WorkItems",
                  "scopes": [
                    {
                      "type": "wiql",
                      "parameters": {
                        "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project",
                        "includeRevisions": true,
                        "includeLinks": false,
                        "includeAttachments": true
                      }
                    }
                  ]
                }
              ]
            }
            """;

        var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts)!;
        var scope = opts.Modules[0].Scopes[0];

        Assert.AreEqual("wiql", scope.Type);
        Assert.IsTrue(scope.Parameters.ContainsKey("query"));
        Assert.IsTrue(scope.Parameters.ContainsKey("includeRevisions"));
        Assert.IsTrue(scope.Parameters.ContainsKey("includeLinks"));
        Assert.IsTrue(scope.Parameters.ContainsKey("includeAttachments"));

        Assert.AreEqual(JsonValueKind.True, scope.Parameters["includeRevisions"].ValueKind);
        Assert.AreEqual(JsonValueKind.False, scope.Parameters["includeLinks"].ValueKind);
        Assert.AreEqual(JsonValueKind.True, scope.Parameters["includeAttachments"].ValueKind);
    }

    [TestMethod]
    public void Deserialize_MultipleModules_AllDeserialized()
    {
        var json = """
            {
              "configVersion": "1.0",
              "mode": "Export",
              "artefacts": { "path": "D:\\exports" },
              "modules": [
                { "name": "WorkItems", "enabled": true, "scopes": [] },
                { "name": "Pipelines", "enabled": false, "scopes": [] }
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
