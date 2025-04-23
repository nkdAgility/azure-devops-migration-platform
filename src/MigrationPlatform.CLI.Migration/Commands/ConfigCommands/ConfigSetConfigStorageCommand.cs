using Microsoft.Extensions.Options;
using MigrationPlatform.CLI.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MigrationPlatform.CLI.ConfigCommands
{
    public class ConfigSetConfigStorageCommand : Command<ConfigSetConfigStorageCommand.Settings>
    {
        private readonly MigrationPlatformOptions _platformOptions;

        public ConfigSetConfigStorageCommand(IOptions<MigrationPlatformOptions> platformOptions)
        {
            _platformOptions = platformOptions.Value;
        }

        public class Settings : CommandSettings
        {
            [CommandOption("--path <PATH>")]
            [Description("Root path to the configuration repository folder (default: ./config)")]
            public string ConfigPath { get; set; } = "./config";

            [CommandOption("--backup")]
            [Description("Backup appsettings.json before modifying it")]
            public bool Backup { get; set; }

            public override ValidationResult Validate()
            {
                return string.IsNullOrWhiteSpace(ConfigPath)
                    ? ValidationResult.Error("--path is required.")
                    : ValidationResult.Success();
            }
        }

        public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
        {
            var configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            if (!File.Exists(configFilePath))
            {
                AnsiConsole.MarkupLine("[red]❌ appsettings.json not found.[/]");
                return 1;
            }

            try
            {
                // Expand both old and new paths
                var oldPath = Environment.ExpandEnvironmentVariables(_platformOptions.Storage);
                var newPath = Environment.ExpandEnvironmentVariables(settings.ConfigPath);

                if (!Path.IsPathRooted(newPath))
                    newPath = Path.GetFullPath(newPath);

                Directory.CreateDirectory(newPath);

                if (settings.Backup)
                {
                    var backupPath = Path.Combine(AppContext.BaseDirectory, "appsettings.backup.json");
                    File.Copy(configFilePath, backupPath, overwrite: true);
                    AnsiConsole.MarkupLineInterpolated($"[yellow]📦 Backup saved to:[/] {backupPath}");
                }

                // Move files if old and new paths differ and old contains files
                if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(oldPath))
                {
                    var files = Directory.GetFiles(oldPath, "*", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        foreach (var file in files)
                        {
                            var relativePath = Path.GetRelativePath(oldPath, file);
                            var destination = Path.Combine(newPath, relativePath);
                            var destinationDir = Path.GetDirectoryName(destination);
                            if (destinationDir != null && !Directory.Exists(destinationDir))
                            {
                                Directory.CreateDirectory(destinationDir);
                            }
                            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                            {
                                Directory.CreateDirectory(destinationDir);
                            }
                            File.Move(file, destination, overwrite: true);
                        }

                        AnsiConsole.MarkupLineInterpolated($"[yellow]↪ Moved {files.Length} files from {oldPath} to {newPath}[/]");
                    }
                }

                // Update appsettings.json
                var json = File.ReadAllText(configFilePath);
                var root = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

                Dictionary<string, string> updatedSection;
                if (root.TryGetValue("MigrationPlatformCli", out var sectionObj) &&
                    sectionObj is JsonElement sectionElement)
                {
                    updatedSection = JsonSerializer.Deserialize<Dictionary<string, string>>(sectionElement.GetRawText())!;
                }
                else
                {
                    updatedSection = new Dictionary<string, string>();
                }

                updatedSection["Storage"] = settings.ConfigPath;
                root["MigrationPlatformCli"] = updatedSection;

                var updatedJson = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFilePath, updatedJson);

                AnsiConsole.MarkupLineInterpolated($"[green]✅ Updated storage path to:[/] {settings.ConfigPath}");
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]❌ Failed to update appsettings.json: {ex.Message}[/]");
                return 1;
            }
        }

    }
}
