// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Teams;

/// <summary>
/// Contract tests for the <see cref="TeamAreaPaths"/> package serialisation shape.
/// The canonical shape mirrors the Azure DevOps <c>teamsettings/teamfieldvalues</c> REST
/// contract (api-version 7.1): <c>field.referenceName</c> + <c>defaultValue</c> +
/// <c>values[{value, includeChildren}]</c>. Legacy package shapes
/// (<c>defaultAreaPath</c>/<c>includedAreaPaths</c> with bare strings) must continue to
/// deserialise, defaulting <c>includeChildren</c> to <c>true</c> — the platform's
/// historical write behaviour.
/// </summary>
[TestClass]
public sealed class TeamAreaPathsContractTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Serialize_WritesApiShapedTeamFieldValuesBlock()
    {
        // Arrange — mixed includeChildren, mirroring Microsoft's own teamfieldvalues example
        var model = new TeamAreaPaths(
            "Proj\\Area",
            new List<TeamFieldValueEntry>
            {
                new("Proj\\Area", IncludeChildren: false),
                new("Proj\\Area\\Sub", IncludeChildren: true)
            });

        // Act
        var json = JsonSerializer.Serialize(model);

        // Assert — exact REST contract property names and per-entry includeChildren
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.AreEqual("System.AreaPath", root.GetProperty("field").GetProperty("referenceName").GetString(),
            "field.referenceName must default to System.AreaPath");
        Assert.AreEqual("Proj\\Area", root.GetProperty("defaultValue").GetString());
        var values = root.GetProperty("values");
        Assert.AreEqual(2, values.GetArrayLength());
        Assert.AreEqual("Proj\\Area", values[0].GetProperty("value").GetString());
        Assert.IsFalse(values[0].GetProperty("includeChildren").GetBoolean(),
            "includeChildren=false ('Exclude sub areas') must be written verbatim");
        Assert.AreEqual("Proj\\Area\\Sub", values[1].GetProperty("value").GetString());
        Assert.IsTrue(values[1].GetProperty("includeChildren").GetBoolean());
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Deserialize_ApiShape_PreservesIncludeChildrenVerbatim()
    {
        // Arrange — API-shaped block as written by external producers (e.g. the SLB
        // Subsurface simulation package) and by this platform going forward
        const string json = """
            {
              "field": { "referenceName": "System.AreaPath" },
              "defaultValue": "Subsurface\\Petrel",
              "values": [
                { "value": "Subsurface\\Petrel", "includeChildren": false },
                { "value": "Subsurface\\Petrel\\Modelling", "includeChildren": true }
              ]
            }
            """;

        // Act
        var model = JsonSerializer.Deserialize<TeamAreaPaths>(json);

        // Assert
        Assert.IsNotNull(model);
        Assert.AreEqual("Subsurface\\Petrel", model!.DefaultValue);
        Assert.AreEqual("System.AreaPath", model.FieldReferenceName);
        Assert.AreEqual(2, model.Values.Count);
        Assert.AreEqual("Subsurface\\Petrel", model.Values[0].Value);
        Assert.IsFalse(model.Values[0].IncludeChildren,
            "'Exclude sub areas' entries must not be widened on read");
        Assert.IsTrue(model.Values[1].IncludeChildren);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Deserialize_LegacyShape_DefaultsIncludeChildrenTrue()
    {
        // Arrange — legacy package shape written by pre-API-shaped exports
        const string json = """
            {
              "defaultAreaPath": "ProjectA",
              "includedAreaPaths": [ "ProjectA", "ProjectA\\Sub" ]
            }
            """;

        // Act
        var model = JsonSerializer.Deserialize<TeamAreaPaths>(json);

        // Assert — legacy entries carry includeChildren=true, matching the platform's
        // historical hardcoded write behaviour
        Assert.IsNotNull(model);
        Assert.AreEqual("ProjectA", model!.DefaultValue);
        Assert.AreEqual("System.AreaPath", model.FieldReferenceName);
        Assert.AreEqual(2, model.Values.Count);
        Assert.AreEqual("ProjectA", model.Values[0].Value);
        Assert.IsTrue(model.Values[0].IncludeChildren);
        Assert.AreEqual("ProjectA\\Sub", model.Values[1].Value);
        Assert.IsTrue(model.Values[1].IncludeChildren);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Deserialize_BareStringValues_DefaultsIncludeChildrenTrue()
    {
        // Arrange — hand-authored packages may write values as bare strings
        const string json = """
            {
              "defaultValue": "ProjectA",
              "values": [ "ProjectA", "ProjectA\\Sub" ]
            }
            """;

        // Act
        var model = JsonSerializer.Deserialize<TeamAreaPaths>(json);

        // Assert
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model!.Values.Count);
        Assert.IsTrue(model.Values[0].IncludeChildren);
        Assert.IsTrue(model.Values[1].IncludeChildren);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Deserialize_EntryWithoutIncludeChildren_DefaultsTrue()
    {
        // Arrange — an object entry that omits includeChildren
        const string json = """
            {
              "defaultValue": "ProjectA",
              "values": [ { "value": "ProjectA" } ]
            }
            """;

        // Act
        var model = JsonSerializer.Deserialize<TeamAreaPaths>(json);

        // Assert
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Values.Count);
        Assert.IsTrue(model.Values[0].IncludeChildren);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Deserialize_CustomTeamFieldReferenceName_IsCarried()
    {
        // Arrange — on-prem servers can use a custom team field instead of System.AreaPath
        const string json = """
            {
              "field": { "referenceName": "Custom.Team" },
              "defaultValue": "Alpha",
              "values": [ { "value": "Alpha", "includeChildren": true } ]
            }
            """;

        // Act
        var model = JsonSerializer.Deserialize<TeamAreaPaths>(json);

        // Assert
        Assert.IsNotNull(model);
        Assert.AreEqual("Custom.Team", model!.FieldReferenceName);
        Assert.IsFalse(model.IsAreaPathField, "a custom team field is not the area path field");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void RoundTrip_PreservesFieldDefaultAndEntries()
    {
        // Arrange
        var model = new TeamAreaPaths(
            "Proj\\Area",
            new List<TeamFieldValueEntry>
            {
                new("Proj\\Area", IncludeChildren: false),
                new("Proj\\Other", IncludeChildren: true)
            },
            "Custom.Team");

        // Act
        var roundTripped = JsonSerializer.Deserialize<TeamAreaPaths>(JsonSerializer.Serialize(model));

        // Assert
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(model.DefaultValue, roundTripped!.DefaultValue);
        Assert.AreEqual(model.FieldReferenceName, roundTripped.FieldReferenceName);
        Assert.AreEqual(model.Values.Count, roundTripped.Values.Count);
        for (var i = 0; i < model.Values.Count; i++)
        {
            Assert.AreEqual(model.Values[i].Value, roundTripped.Values[i].Value);
            Assert.AreEqual(model.Values[i].IncludeChildren, roundTripped.Values[i].IncludeChildren);
        }
    }
}
