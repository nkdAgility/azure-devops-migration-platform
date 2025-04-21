using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigrationPlatform.CLI.Commands
{
    public class AdoSettings : CommandSettings
    {
        [CommandOption("--organisation <organisation>")]
        [Description("Name of the config location to use (default: default)")]
        public string Organisation { get; set; } = "default";

        [CommandOption("--token <token>")]
        [Description("Name of the config location to use (default: default)")]
        public string Token { get; set; } = "default";
    }
}
