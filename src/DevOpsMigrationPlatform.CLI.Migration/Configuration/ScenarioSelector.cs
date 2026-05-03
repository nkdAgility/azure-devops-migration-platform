// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Migration.Configuration;

/// <summary>
/// Resolves a configuration file path when <c>--config</c> is not provided.
/// Follows a well-defined fallback chain and, when run interactively, presents
/// a <see cref="SelectionPrompt{T}"/> so the operator can pick a scenario.
///
/// <para>Resolution order (highest priority first):</para>
/// <list type="number">
///   <item><c>$Env:MigrationPlatform_Scenario_Folder</c></item>
///   <item><c>preferences.json → scenario-folder</c></item>
///   <item><c>./scenarios</c> subfolder of cwd</item>
///   <item><c>*.json</c> in cwd</item>
/// </list>
/// </summary>
internal static class ScenarioSelector
{
    private const string EnvVarName = "MigrationPlatform_Scenario_Folder";

    /// <summary>
    /// Attempts to resolve a config file interactively when <c>--config</c>
    /// was not supplied. Returns <c>null</c> when no candidates were found.
    /// </summary>
    /// <param name="console">
    /// The Spectre.Console instance used to present the selection prompt.
    /// </param>
    /// <returns>Full path to the chosen config file, or <c>null</c>.</returns>
    public static string? PromptForConfigFile(IAnsiConsole console)
    {
        // 1. Environment variable
        var envFolder = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(envFolder))
        {
            var result = PromptFromFolder(console, envFolder);
            if (result is not null) return result;
        }

        // 2. preferences.json → scenario-folder
        var prefFolder = UserPreferencesService.Get("scenario-folder");
        if (!string.IsNullOrWhiteSpace(prefFolder))
        {
            var result = PromptFromFolder(console, prefFolder);
            if (result is not null) return result;
        }

        // 3. ./scenarios subfolder
        var scenariosDir = Path.Combine(Directory.GetCurrentDirectory(), "scenarios");
        if (Directory.Exists(scenariosDir))
        {
            var result = PromptFromFolder(console, scenariosDir);
            if (result is not null) return result;
        }

        // 4. *.json in cwd
        var cwdFiles = ScanJsonFiles(Directory.GetCurrentDirectory());
        if (cwdFiles.Count > 0)
            return PromptSelection(console, cwdFiles);

        return null;
    }

    /// <summary>
    /// Scans a folder for <c>*.json</c> files and presents a selection prompt.
    /// Returns <c>null</c> when the folder does not exist or contains no JSON files.
    /// </summary>
    private static string? PromptFromFolder(IAnsiConsole console, string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        var files = ScanJsonFiles(folder);
        if (files.Count == 0)
            return null;

        if (files.Count == 1)
        {
            var singleFile = files[0];
            console.MarkupLine($"[blue]ℹ[/] Using config: [cyan]{Path.GetFileName(singleFile)}[/]");
            return singleFile;
        }

        return PromptSelection(console, files);
    }

    /// <summary>
    /// Scans a directory (non-recursive) for <c>*.json</c> files, sorted by name.
    /// </summary>
    private static List<string> ScanJsonFiles(string directory)
    {
        try
        {
            return Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>
    /// Presents a Spectre.Console selection prompt for the given config files.
    /// Returns <c>null</c> when the terminal is not interactive (e.g. CI subprocess
    /// with redirected stdout/stderr).
    /// </summary>
    private static string? PromptSelection(IAnsiConsole console, List<string> files)
    {
        // Spectre.Console throws "Cannot show selection prompt since the current terminal
        // isn't interactive" when stdout/stderr is redirected (CI, subprocess tests, etc.).
        // Always check Interactive before calling Prompt() on any interactive widget.
        if (!console.Profile.Capabilities.Interactive)
            return null;

        var prompt = new SelectionPrompt<string>()
            .Title("Select a configuration file:")
            .PageSize(15)
            .MoreChoicesText("[grey](Move up and down to reveal more files)[/]")
            .AddChoices(files.Select(f => Path.GetRelativePath(Directory.GetCurrentDirectory(), f)));

        var selected = console.Prompt(prompt);

        // Resolve back to absolute path
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), selected));
    }
}
