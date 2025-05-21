using MigrationPlatform.Infrastructure.TfsObjectModel;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;

namespace MigrationPlatform.CLI.ConfigCommands
{
    public class ConfigCreateCommand : Command<ConfigCreateCommand.Settings>
    {

        public ConfigCreateCommand()
        {

        }


        public class Settings : CommandSettings
        {

        }

        public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
        {
            var host = MigrationPlatformHost.CreateDefaultBuilder().Build();

            AnsiConsole.MarkupLine("[grey]Please specify a subcommand. Try 'catalog plan' or 'catalog list'.[/]");
            return 1;
        }
    }
}
