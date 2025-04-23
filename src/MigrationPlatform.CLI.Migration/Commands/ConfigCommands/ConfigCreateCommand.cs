using Microsoft.Extensions.Options;
using MigrationPlatform.CLI.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;

namespace MigrationPlatform.CLI.ConfigCommands
{
    public class ConfigCreateCommand : Command<ConfigCreateCommand.Settings>
    {

        private readonly MigrationPlatformOptions _platformOptions;

        public ConfigCreateCommand(IOptions<MigrationPlatformOptions> platformOptions)
        {
            _platformOptions = platformOptions.Value;
        }


        public class Settings : CommandSettings
        {

        }

        public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
        {
            AnsiConsole.MarkupLine("[grey]Please specify a subcommand. Try 'catalog plan' or 'catalog list'.[/]");
            return 1;
        }
    }
}
