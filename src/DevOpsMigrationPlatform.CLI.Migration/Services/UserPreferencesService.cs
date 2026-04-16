using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevOpsMigrationPlatform.CLI.Migration.Services;

/// <summary>
/// Reads and writes user-level preferences stored in a lightweight JSON file
/// under the user's application-data directory.
///
/// <para>
///   Windows:  %APPDATA%\nkdAgility\devopsmigration\preferences.json
///   Linux/macOS: ~/.config/devopsmigration/preferences.json
/// </para>
///
/// This service is intentionally pre-DI — it can be called from static helpers
/// (e.g. <see cref="MigrationPlatformHost.ExtractConfigFileArg"/>) that run
/// before the host is built.
/// </summary>
internal sealed class UserPreferencesService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Returns the directory that holds <c>preferences.json</c>.
    /// </summary>
    internal static string GetPreferencesDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(appData))
        {
            // Fallback for environments where ApplicationData is not set
            appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(appData, "nkdAgility", "devopsmigration")
            : Path.Combine(appData, "devopsmigration");
    }

    /// <summary>
    /// Full path to <c>preferences.json</c>.
    /// </summary>
    internal static string GetPreferencesFilePath()
        => Path.Combine(GetPreferencesDirectory(), "preferences.json");

    /// <summary>
    /// Loads the current preferences from disk, or returns an empty model when
    /// the file does not exist.
    /// </summary>
    public static UserPreferences Load()
    {
        var path = GetPreferencesFilePath();
        if (!File.Exists(path))
            return new UserPreferences();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserPreferences>(json, SerializerOptions)
                   ?? new UserPreferences();
        }
        catch (JsonException)
        {
            // Corrupt file — treat as empty rather than crashing the CLI.
            return new UserPreferences();
        }
    }

    /// <summary>
    /// Persists the given preferences to disk, creating the directory tree if
    /// it does not exist.
    /// </summary>
    public static void Save(UserPreferences preferences)
    {
        var path = GetPreferencesFilePath();
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(preferences, SerializerOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Gets a preference value by key name (case-insensitive).
    /// </summary>
    /// <returns>The value, or <c>null</c> when the key is not set.</returns>
    public static string? Get(string key)
    {
        var prefs = Load();
        return key.ToLowerInvariant() switch
        {
            "scenario-folder" => prefs.ScenarioFolder,
            _ => null
        };
    }

    /// <summary>
    /// Sets a preference value by key name (case-insensitive).
    /// </summary>
    /// <returns><c>true</c> when the key was recognised and set; <c>false</c> otherwise.</returns>
    public static bool Set(string key, string value)
    {
        var prefs = Load();
        switch (key.ToLowerInvariant())
        {
            case "scenario-folder":
                prefs.ScenarioFolder = value;
                break;
            default:
                return false;
        }

        Save(prefs);
        return true;
    }

    /// <summary>
    /// Returns all supported preference keys.
    /// </summary>
    public static IReadOnlyList<string> SupportedKeys => ["scenario-folder"];
}

/// <summary>
/// Strongly-typed model for the user preferences file.
/// </summary>
internal sealed class UserPreferences
{
    public string? ScenarioFolder { get; set; }
}
