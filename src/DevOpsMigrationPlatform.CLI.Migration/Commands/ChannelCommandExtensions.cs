using System.Reflection;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Extension helpers that wrap Spectre.Console's <c>AddCommand</c>, automatically
/// calling <c>.IsHidden()</c> when the command class carries a
/// <see cref="HideFromChannelAttribute"/> whose threshold is met by the current
/// <see cref="ReleaseChannelDetector.Current"/> value.
///
/// Commands without the attribute are always visible — these extensions are safe
/// to use unconditionally for every command registration in Program.cs.
/// </summary>
internal static class ChannelCommandExtensions
{
    /// <summary>Registers <typeparamref name="TCommand"/> on the root configurator.</summary>
    public static ICommandConfigurator AddChannelCommand<TCommand>(
        this IConfigurator config, string name)
        where TCommand : class, ICommand
    {
        var cmd = config.AddCommand<TCommand>(name);
        HideIfNeeded<TCommand>(cmd);
        return cmd;
    }

    /// <summary>Registers <typeparamref name="TCommand"/> on a branch configurator.</summary>
    public static ICommandConfigurator AddChannelCommand<TCommand>(
        this IConfigurator<CommandSettings> config, string name)
        where TCommand : class, ICommand
    {
        var cmd = config.AddCommand<TCommand>(name);
        HideIfNeeded<TCommand>(cmd);
        return cmd;
    }

    private static void HideIfNeeded<TCommand>(ICommandConfigurator cmd)
        where TCommand : class
    {
        var attr = typeof(TCommand).GetCustomAttribute<HideFromChannelAttribute>();
        if (attr is not null && ReleaseChannelDetector.Current >= attr.Channel)
            cmd.IsHidden();
    }
}
