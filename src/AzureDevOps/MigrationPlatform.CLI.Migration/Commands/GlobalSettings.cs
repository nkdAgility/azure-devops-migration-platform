using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigrationPlatform.CLI.Commands
{
    public class GlobalSettings : CommandSettings
    {
        [CommandOption("--config <PATH>")]
        [Description("Name of the config location to use (default: default)")]
        public string ConfigurationName { get; set; } = "default";

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(ConfigurationName)) return ValidationResult.Error("Configuration is required");
            return ValidationResult.Success();
        }

    }
}
