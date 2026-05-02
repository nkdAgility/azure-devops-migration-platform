// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

/// <summary>
/// Verifies that <see cref="MigrationOptions"/> correctly deserializes the typed
/// <c>Modules</c> object from JSON and that defaults are applied when keys are absent.
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
  public void Deserialize_WithoutModulesKey_DefaultsToEnabledWorkItems()
  {
    var json = """
            {
              "configVersion": "2.0",
              "mode": "Export",
              "artefacts": { "workingDirectory": "D:\\exports" }
            }
            """;

    var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts);

    Assert.IsNotNull(opts);
    Assert.IsNotNull(opts.Modules);
    Assert.IsNotNull(opts.Modules.WorkItems);
    Assert.IsTrue(opts.Modules.WorkItems.Enabled);
  }

  [TestMethod]
  public void Deserialize_WithTypedModulesObject_PopulatesWorkItems()
  {
    var json = """
            {
              "configVersion": "2.0",
              "mode": "Export",
              "artefacts": { "workingDirectory": "D:\\exports" },
              "modules": {
                "workitems": {
                  "enabled": true,
                  "scope": {
                    "query": "SELECT [System.Id] FROM WorkItems"
                  },
                  "extensions": {
                    "revisions": { "enabled": true }
                  }
                }
              }
            }
            """;

    var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts);

    Assert.IsNotNull(opts);
    var wi = opts.Modules.WorkItems;
    Assert.IsTrue(wi.Enabled);
    Assert.AreEqual("SELECT [System.Id] FROM WorkItems", wi.Scope.Query);
    Assert.IsTrue(wi.Extensions.Revisions.Enabled);
  }

  [TestMethod]
  public void Deserialize_WorkItemsExtensions_AllPresent()
  {
    var json = """
            {
              "configVersion": "2.0",
              "mode": "Export",
              "artefacts": { "workingDirectory": "D:\\exports" },
              "modules": {
                "workitems": {
                  "scope": {
                    "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project"
                  },
                  "extensions": {
                    "revisions": { "enabled": true },
                    "links": { "enabled": true },
                    "attachments": { "enabled": false },
                    "comments": { "enabled": true, "includeDeleted": true },
                    "embeddedImages": { "enabled": true, "downloadTimeoutSeconds": 45 }
                  }
                }
              }
            }
            """;

    var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts)!;
    var wi = opts.Modules.WorkItems;

    Assert.AreEqual("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project",
        wi.Scope.Query);

    Assert.IsTrue(wi.Extensions.Revisions.Enabled);
    Assert.IsTrue(wi.Extensions.Links.Enabled);
    Assert.IsFalse(wi.Extensions.Attachments.Enabled);
    Assert.IsTrue(wi.Extensions.Comments.Enabled);
    Assert.IsTrue(wi.Extensions.Comments.IncludeDeleted);
    Assert.IsTrue(wi.Extensions.EmbeddedImages.Enabled);
    Assert.AreEqual(45, wi.Extensions.EmbeddedImages.DownloadTimeoutSeconds);
  }

  [TestMethod]
  public void Deserialize_WorkItemsDisabled_ReflectsEnabledFalse()
  {
    var json = """
            {
              "configVersion": "2.0",
              "mode": "Export",
              "artefacts": { "workingDirectory": "D:\\exports" },
              "modules": {
                "workitems": {
                  "enabled": false
                }
              }
            }
            """;

    var opts = JsonSerializer.Deserialize<MigrationOptions>(json, JsonOpts)!;

    Assert.IsFalse(opts.Modules.WorkItems.Enabled);
  }
}
