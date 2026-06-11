// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Config;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Config;

[TestClass]
public sealed class JsonSchemaConfigValidatorTests
{
  private string _tempSchemaPath = string.Empty;

  [TestInitialize]
  public void Setup()
  {
    _tempSchemaPath = Path.Combine(Path.GetTempPath(), $"test-schema-{Guid.NewGuid()}.json");
  }

  [TestCleanup]
  public void Cleanup()
  {
    if (File.Exists(_tempSchemaPath))
    {
      File.Delete(_tempSchemaPath);
    }
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
  [TestMethod]
  public void Validate_ValidJson_ReturnsEmptyList()
  {
    var schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""name"": { ""type"": ""string"" }
  },
  ""required"": [""name""],
  ""additionalProperties"": false
}";
    File.WriteAllText(_tempSchemaPath, schema);

    var validator = CreateValidator(_tempSchemaPath);
    var json = @"{""name"": ""test""}";

    var errors = validator.Validate(json);

    Assert.AreEqual(0, errors.Count);
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
  [TestMethod]
  public void Validate_UnknownKey_ReturnsError()
  {
    var schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""name"": { ""type"": ""string"" }
  },
  ""additionalProperties"": false
}";
    File.WriteAllText(_tempSchemaPath, schema);

    var validator = CreateValidator(_tempSchemaPath);
    var json = @"{""name"": ""test"", ""unknownKey"": ""value""}";

    var errors = validator.Validate(json);

    Assert.IsTrue(errors.Count > 0);
    Assert.IsTrue(errors.Any(e => e.JsonPath.Contains("unknownKey") || e.JsonPath == "#"));
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
  [TestMethod]
  public void Validate_MissingRequiredField_ReturnsError()
  {
    var schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""name"": { ""type"": ""string"" }
  },
  ""required"": [""name""]
}";
    File.WriteAllText(_tempSchemaPath, schema);

    var validator = CreateValidator(_tempSchemaPath);
    var json = @"{}";

    var errors = validator.Validate(json);

    Assert.IsTrue(errors.Count > 0);
    var error = errors.First();
    Assert.IsNotNull(error.JsonPath);
    Assert.IsNotNull(error.Constraint);
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
  [TestMethod]
  public void Validate_WrongType_ReturnsError()
  {
    var schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""age"": { ""type"": ""integer"" }
  }
}";
    File.WriteAllText(_tempSchemaPath, schema);

    var validator = CreateValidator(_tempSchemaPath);
    var json = @"{""age"": ""not a number""}";

    var errors = validator.Validate(json);

    Assert.IsTrue(errors.Count > 0);
    Assert.IsTrue(errors.Any(e => e.JsonPath.Contains("age")));
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
  [TestMethod]
  public void Validate_SchemaFileDoesNotExist_ReturnsEmptyList()
  {
    var nonExistentPath = Path.Combine(Path.GetTempPath(), $"non-existent-{Guid.NewGuid()}.json");
    var validator = CreateValidator(nonExistentPath);
    var json = @"{""anything"": ""goes""}";

    var errors = validator.Validate(json);

    Assert.AreEqual(0, errors.Count);
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
  [TestMethod]
  public void Validate_MultipleErrors_ReturnsAll()
  {
    var schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""name"": { ""type"": ""string"" },
    ""age"": { ""type"": ""integer"" }
  },
  ""required"": [""name"", ""age""],
  ""additionalProperties"": false
}";
    File.WriteAllText(_tempSchemaPath, schema);

    var validator = CreateValidator(_tempSchemaPath);
    var json = @"{""unknownKey"": ""value""}";

    var errors = validator.Validate(json);

    Assert.IsTrue(errors.Count >= 2);
  }

  [TestCategory("CodeTest")]
  [TestCategory("IntegrationTests")]
  [TestMethod]
  public void Validate_NestedObject_ValidatesCorrectly()
  {
    var schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""config"": {
      ""type"": ""object"",
      ""properties"": {
        ""enabled"": { ""type"": ""boolean"" }
      },
      ""required"": [""enabled""]
    }
  }
}";
    File.WriteAllText(_tempSchemaPath, schema);

    var validator = CreateValidator(_tempSchemaPath);
    var json = @"{""config"": {""enabled"": true}}";

    var errors = validator.Validate(json);

    Assert.AreEqual(0, errors.Count);
  }

  private static JsonSchemaConfigValidator CreateValidator(string schemaPath)
  {
    var options = Options.Create(new JsonSchemaConfigValidatorOptions
    {
      SchemaPath = schemaPath
    });

    return new JsonSchemaConfigValidator(options);
  }
}
