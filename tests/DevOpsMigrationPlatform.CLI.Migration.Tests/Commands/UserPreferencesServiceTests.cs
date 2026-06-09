// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class UserPreferencesServiceTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"prefs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        UserPreferencesService.OverridePreferencesDirectory = _tempDir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        UserPreferencesService.OverridePreferencesDirectory = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Load_WhenFileDoesNotExist_ReturnsEmptyPreferences()
    {
        // Act
        var prefs = UserPreferencesService.Load();

        // Assert
        Assert.IsNotNull(prefs);
        Assert.IsNull(prefs.ScenarioFolder);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Set_WithSupportedKey_PersistsValue()
    {
        // Act
        var result = UserPreferencesService.Set("scenario-folder", "/some/path");

        // Assert
        Assert.IsTrue(result);
        var loaded = UserPreferencesService.Load();
        Assert.AreEqual("/some/path", loaded.ScenarioFolder);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Set_WithUnsupportedKey_ReturnsFalse()
    {
        // Act
        var result = UserPreferencesService.Set("unknown-key", "value");

        // Assert
        Assert.IsFalse(result);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Get_WithSetKey_ReturnsValue()
    {
        // Arrange
        UserPreferencesService.Set("scenario-folder", "/test/dir");

        // Act
        var value = UserPreferencesService.Get("scenario-folder");

        // Assert
        Assert.AreEqual("/test/dir", value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Get_WithUnsetKey_ReturnsNull()
    {
        // Act
        var value = UserPreferencesService.Get("scenario-folder");

        // Assert
        Assert.IsNull(value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Get_WithUnsupportedKey_ReturnsNull()
    {
        // Act
        var value = UserPreferencesService.Get("nonexistent");

        // Assert
        Assert.IsNull(value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Set_IsCaseInsensitive()
    {
        // Act
        var result = UserPreferencesService.Set("SCENARIO-FOLDER", "/test");

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual("/test", UserPreferencesService.Get("scenario-folder"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Save_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var prefs = new UserPreferences { ScenarioFolder = "/some/path" };

        // Act
        UserPreferencesService.Save(prefs);

        // Assert
        Assert.IsTrue(File.Exists(UserPreferencesService.GetPreferencesFilePath()));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Load_WithCorruptFile_ReturnsEmptyPreferences()
    {
        // Arrange
        var path = UserPreferencesService.GetPreferencesFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not valid json{{{");

        // Act
        var prefs = UserPreferencesService.Load();

        // Assert
        Assert.IsNotNull(prefs);
        Assert.IsNull(prefs.ScenarioFolder);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void SupportedKeys_ContainsScenarioFolder()
    {
        // Assert
        Assert.IsTrue(UserPreferencesService.SupportedKeys.Contains("scenario-folder"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Set_OverwritesExistingValue()
    {
        // Arrange
        UserPreferencesService.Set("scenario-folder", "/first");

        // Act
        UserPreferencesService.Set("scenario-folder", "/second");

        // Assert
        Assert.AreEqual("/second", UserPreferencesService.Get("scenario-folder"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetPreferencesDirectory_WithOverride_ReturnsOverridePath()
    {
        // Act
        var dir = UserPreferencesService.GetPreferencesDirectory();

        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(dir));
        Assert.AreEqual(_tempDir, dir);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void GetPreferencesDirectory_WithoutOverride_ContainsDevopsmigration()
    {
        // Arrange
        UserPreferencesService.OverridePreferencesDirectory = null;

        // Act
        var dir = UserPreferencesService.GetPreferencesDirectory();

        // Assert
        Assert.IsFalse(string.IsNullOrEmpty(dir));
        Assert.IsTrue(dir.Contains("devopsmigration"));
    }
}
