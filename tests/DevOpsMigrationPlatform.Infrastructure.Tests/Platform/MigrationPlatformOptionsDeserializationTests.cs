// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

/// <summary>
/// Verifies that <see cref="MigrationPlatformOptions"/> correctly deserializes the typed
/// <c>Modules</c> object from JSON and that defaults are applied when keys are absent.
/// </summary>
[TestClass]
public class MigrationPlatformOptionsDeserializationTests
{
  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = { new JsonStringEnumConverter() }
  };

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
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

    var opts = JsonSerializer.Deserialize<MigrationPlatformOptions>(json, JsonOpts);

    Assert.IsNotNull(opts);
    Assert.IsNotNull(opts.Modules);
    Assert.IsNotNull(opts.Modules.WorkItems);
    Assert.IsTrue(opts.Modules.WorkItems.Enabled);
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
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
                  "selection": {
                    "query": "SELECT [System.Id] FROM WorkItems"
                  },
                  "data": {
                    "revisions": { "enabled": true }
                  }
                }
              }
            }
            """;

    var opts = JsonSerializer.Deserialize<MigrationPlatformOptions>(json, JsonOpts);

    Assert.IsNotNull(opts);
    var wi = opts.Modules.WorkItems;
    Assert.IsTrue(wi.Enabled);
    Assert.AreEqual("SELECT [System.Id] FROM WorkItems", wi.Selection.Query);
    Assert.IsTrue(wi.Data.Revisions.Enabled);
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
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
                  "selection": {
                    "query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project"
                  },
                  "data": {
                    "revisions": { "enabled": true },
                    "comments": { "enabled": true, "includeDeleted": true },
                    "embeddedImages": { "enabled": true, "downloadTimeoutSeconds": 45 }
                  },
                  "processing": {
                    "workItemResolutionStrategy": { "enabled": true, "strategy": "TargetField", "fieldName": "Custom.ReflectedWorkItemId" }
                  }
                }
              }
            }
            """;

    var opts = JsonSerializer.Deserialize<MigrationPlatformOptions>(json, JsonOpts)!;
    var wi = opts.Modules.WorkItems;

    Assert.AreEqual("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project",
        wi.Selection.Query);

    Assert.IsTrue(wi.Data.Revisions.Enabled);
    Assert.IsTrue(wi.Data.Comments.Enabled);
    Assert.IsTrue(wi.Data.Comments.IncludeDeleted);
    Assert.IsTrue(wi.Data.EmbeddedImages.Enabled);
    Assert.AreEqual(45, wi.Data.EmbeddedImages.DownloadTimeoutSeconds);
    Assert.AreEqual("TargetField", wi.Processing.WorkItemResolutionStrategy.Strategy);
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
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

    var opts = JsonSerializer.Deserialize<MigrationPlatformOptions>(json, JsonOpts)!;

    Assert.IsFalse(opts.Modules.WorkItems.Enabled);
  }
}
