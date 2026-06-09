// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.SchemaValidation;

[TestClass]
public sealed class SchemaValidationObservabilityTests
{
    private string? _tempSchemaPath;

    [TestCleanup]
    public void Cleanup()
    {
        if (_tempSchemaPath != null && File.Exists(_tempSchemaPath))
            File.Delete(_tempSchemaPath);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void Validate_WithUnknownKey_LogsErrorWithJsonPathAndConstraint()
    {
        // Arrange
        var minimalSchema = new
        {
            type = "object",
            properties = new
            {
                mode = new { type = "string" },
                source = new { type = "object" }
            },
            additionalProperties = false
        };

        _tempSchemaPath = Path.Combine(Path.GetTempPath(), $"schema-{Guid.NewGuid()}.json");
        File.WriteAllText(_tempSchemaPath, JsonSerializer.Serialize(minimalSchema));

        var invalidConfig = new Dictionary<string, object>
        {
            { "mode", "Export" },
            { "source", new Dictionary<string, object> { { "type", "Simulated" } } },
            { "unknownField", "unexpected" }
        };

        var configJson = JsonSerializer.Serialize(invalidConfig);

        var options = MsOptions.Create(new JsonSchemaConfigValidatorOptions
        {
            SchemaPath = _tempSchemaPath
        });

        var validator = new JsonSchemaConfigValidator(options);

        // Act
        var errors = validator.Validate(configJson);

        // Assert
        Assert.IsTrue(errors.Count > 0, "Expected validation errors for unknown key");
        Assert.IsTrue(
            errors[0].JsonPath.Contains("unknownField") || errors[0].JsonPath == "#",
            $"Expected JsonPath to reference 'unknownField', got: {errors[0].JsonPath}");
        Assert.IsFalse(
            string.IsNullOrEmpty(errors[0].Constraint),
            "Expected Constraint to be populated");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void Validate_WithMissingRequired_LogsErrorWithJsonPath()
    {
        // Arrange
        var minimalSchema = new
        {
            type = "object",
            properties = new
            {
                mode = new { type = "string" },
                source = new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string" }
                    },
                    required = new[] { "type" }
                }
            },
            required = new[] { "source" }
        };

        _tempSchemaPath = Path.Combine(Path.GetTempPath(), $"schema-{Guid.NewGuid()}.json");
        File.WriteAllText(_tempSchemaPath, JsonSerializer.Serialize(minimalSchema));

        var invalidConfig = new
        {
            mode = "Export",
            source = new
            {
                url = "https://dev.azure.com/test"
                // Missing: type
            }
        };

        var configJson = JsonSerializer.Serialize(invalidConfig);

        var options = MsOptions.Create(new JsonSchemaConfigValidatorOptions
        {
            SchemaPath = _tempSchemaPath
        });

        var validator = new JsonSchemaConfigValidator(options);

        // Act
        var errors = validator.Validate(configJson);

        // Assert
        Assert.IsTrue(errors.Count > 0, "Expected validation errors for missing required field");
        Assert.IsTrue(
            errors[0].JsonPath.Contains("source") || errors[0].JsonPath.Contains("type"),
            $"Expected JsonPath to reference 'source' or 'type', got: {errors[0].JsonPath}");
        Assert.IsFalse(
            string.IsNullOrEmpty(errors[0].Constraint),
            "Expected Constraint to be populated");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void Validate_WithValidConfig_ReturnsEmptyList()
    {
        // Arrange
        var minimalSchema = new
        {
            type = "object",
            properties = new
            {
                mode = new { type = "string" },
                source = new
                {
                    type = "object",
                    properties = new
                    {
                        type = new { type = "string" }
                    },
                    required = new[] { "type" }
                }
            },
            additionalProperties = false
        };

        _tempSchemaPath = Path.Combine(Path.GetTempPath(), $"schema-{Guid.NewGuid()}.json");
        File.WriteAllText(_tempSchemaPath, JsonSerializer.Serialize(minimalSchema));

        var validConfig = new
        {
            mode = "Export",
            source = new
            {
                type = "Simulated",
                url = "https://dev.azure.com/test"
            }
        };

        var configJson = JsonSerializer.Serialize(validConfig);

        var options = MsOptions.Create(new JsonSchemaConfigValidatorOptions
        {
            SchemaPath = _tempSchemaPath
        });

        var validator = new JsonSchemaConfigValidator(options);

        // Act
        var errors = validator.Validate(configJson);

        // Assert
        Assert.AreEqual(0, errors.Count, "Expected no validation errors for valid config");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void Validate_WithAbsentSchema_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.json");

        var options = MsOptions.Create(new JsonSchemaConfigValidatorOptions
        {
            SchemaPath = nonExistentPath
        });

        var validator = new JsonSchemaConfigValidator(options);

        var validConfig = new
        {
            mode = "Export",
            source = new { type = "Simulated" }
        };

        var configJson = JsonSerializer.Serialize(validConfig);

        // Act
        var errors = validator.Validate(configJson);

        // Assert
        Assert.AreEqual(0, errors.Count, "Expected empty list when schema is absent (proceed with warning)");
    }
}

