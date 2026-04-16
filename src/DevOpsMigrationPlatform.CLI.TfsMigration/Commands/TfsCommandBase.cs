using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.TfsMigration.Commands;

/// <summary>
/// Abstract base class providing common command infrastructure for TFS CLI commands.
/// Mirrors the lifecycle of <c>CommandBase&lt;T&gt;</c> in <c>CLI.Migration</c>:
/// host creation via <see cref="MigrationPlatformHost"/>, service resolution shortcuts,
/// consistent error handling, and host disposal in <c>finally</c>.
///
/// Simpler than the .NET 10 counterpart — no interactive config resolution, no
/// ControlPlane, no Aspire. The TFS subprocess receives all configuration via
/// command-line arguments and stdin JSON.
/// </summary>
/// <typeparam name="TSettings">Command settings type derived from <see cref="CommandSettings"/>.</typeparam>
public abstract class TfsCommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    /// <summary>
    /// The host created by this command. Disposed at the end of execution.
    /// </summary>
    protected IHost? Host { get; private set; }

    /// <summary>
    /// Creates and starts an <see cref="IHost"/> using the shared
    /// <see cref="MigrationPlatformHost.CreateDefaultBuilder"/>. Idempotent — returns
    /// the existing host if already created.
    /// </summary>
    protected async Task<IHost> CreateHost(MigrationPlatformHost.Settings settings, string[] args)
    {
        if (Host is not null)
            return Host;

        Host = MigrationPlatformHost.CreateDefaultBuilder(args, settings).Build();
        await Host.StartAsync().ConfigureAwait(false);
        return Host;
    }

    /// <summary>Resolve a required service from the command's host.</summary>
    protected TService GetRequiredService<TService>() where TService : notnull
        => Host!.Services.GetRequiredService<TService>();

    /// <summary>Resolve an optional service from the command's host.</summary>
    protected TService? GetService<TService>() where TService : class
        => Host?.Services.GetService<TService>();

    /// <summary>
    /// Reads a single JSON line from stdin to extract credentials.
    /// Returns the PAT if present, or <c>null</c> for Windows-integrated auth.
    /// Expected format: <c>{"pat":"&lt;token&gt;"}</c> or <c>{}</c>.
    /// </summary>
    protected static async Task<string?> ReadCredentialsFromStdinAsync()
    {
        try
        {
            var stdinLine = await Console.In.ReadLineAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stdinLine))
            {
                using var doc = JsonDocument.Parse(stdinLine);
                if (doc.RootElement.TryGetProperty("pat", out var patProp))
                    return patProp.GetString();
            }
        }
        catch
        {
            // No stdin or malformed JSON — fall back to Windows-integrated auth.
        }
        return null;
    }

    /// <summary>
    /// Executes the command with consistent error handling and host disposal.
    /// Subclasses implement <see cref="ExecuteInternalAsync"/>.
    /// </summary>
    protected sealed override async Task<int> ExecuteAsync(
        CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteInternalAsync(context, settings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        finally
        {
            if (Host is not null)
            {
                try
                {
                    await Host.StopAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort shutdown — don't mask the original exception.
                }

                if (Host is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else
                    Host.Dispose();
            }
        }
    }

    /// <summary>
    /// Command-specific implementation. Derived classes must call
    /// <see cref="CreateHost"/> to obtain an <see cref="IHost"/> with
    /// their required services.
    /// </summary>
    protected abstract Task<int> ExecuteInternalAsync(
        CommandContext context, TSettings settings, CancellationToken cancellationToken);
}
