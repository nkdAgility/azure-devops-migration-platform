// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.Commands.ControlPlane;
using DevOpsMigrationPlatform.CLI.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Commands.Manage;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        // Wire Ctrl+C and terminal close to the process-wide GlobalCancellation signal.
        // CommandBase.ExecuteAsync links this token with any Spectre.Console-provided token
        // so that all async operations (SSE streams, HTTP calls, LocalStackHost) honour Ctrl+C.
        Console.CancelKeyPress += (_, e) =>
        {
            // First Ctrl+C: cancel gracefully and swallow the signal so the finally
            // blocks in CommandBase.ExecuteAsync have time to run DisposeResourcesAsync.
            // Second Ctrl+C: default behaviour terminates immediately.
            if (!GlobalCancellation.Token.IsCancellationRequested)
            {
                e.Cancel = true;
                GlobalCancellation.Cancel();
                AnsiConsole.MarkupLine("[yellow]Cancelling… press Ctrl+C again to force quit.[/]");
            }
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            GlobalCancellation.Cancel();

            // Give the finally blocks a few seconds to clean up LocalStackHost
            // before the runtime tears down the process.
            GlobalCancellation.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
        };

        AnsiConsole.Write(new FigletText("DevOps Migration").LeftJustified().Color(Color.Blue));
        AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
        var appVersion = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "unknown";
        AnsiConsole.MarkupLine($"[dim]Azure DevOps Migration Platform  v{Markup.Escape(appVersion)}[/]");
        AnsiConsole.MarkupLine($"[dim]Created by Martin Hinshelwood[/]");
        AnsiConsole.WriteLine();

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("devopsmigration");
#if DEBUG
            config.PropagateExceptions();
            config.ValidateExamples();
#endif

            // ── Migration commands (all read their configuration from --config) ──────
            config.AddChannelCommand<PrepareCommand>("prepare")
                .WithDescription("Validate config, compute configHash, print planned modules. No job is submitted.")
                .WithExample("prepare", "--config", "migration.json");

            config.AddChannelCommand<QueueCommand>("queue")
                .WithDescription("Submit a migration job. Behaviour is determined by the 'mode' field in the config (Export, Import, or Both).")
                .WithExample("queue", "--config", "migration.json")
                .WithExample("queue", "--config", "scenarios/queue-export-ado-workitems-single-project.json")
                .WithExample("queue", "--config", "migration.json", "--force-fresh");

            // ── Job management commands ──────────────────────────────────────────────
            config.AddBranch("manage", branch =>
            {
                branch.SetDescription("Query and control existing jobs.");

                branch.AddChannelCommand<ManageListCommand>("list")
                    .WithDescription("List all jobs visible to the authenticated user with status and progress.")
                    .WithExample("manage", "list");

                branch.AddChannelCommand<ManageStatusCommand>("status")
                    .WithDescription("Display job state and per-module progress for a specific job.")
                    .WithExample("manage", "status", "--job", "550e8400-e29b-41d4-a716-446655440000");

                branch.AddChannelCommand<ManageLogsCommand>("logs")
                    .WithDescription("[Deprecated] Use 'manage progress' or 'manage diagnostics' instead.")
                    .WithExample("manage", "logs", "--job", "550e8400-e29b-41d4-a716-446655440000");

                branch.AddChannelCommand<ManageProgressCommand>("progress")
                    .WithDescription("Display a snapshot of ProgressEvent records for a specific job.")
                    .WithExample("manage", "progress", "--job", "550e8400-e29b-41d4-a716-446655440000");

                branch.AddChannelCommand<ManageDiagnosticsCommand>("diagnostics")
                    .WithDescription("Download diagnostic log records from a completed job's package.")
                    .WithExample("manage", "diagnostics", "--job", "550e8400-e29b-41d4-a716-446655440000")
                    .WithExample("manage", "diagnostics", "--job", "550e8400-e29b-41d4-a716-446655440000", "--level", "Warning");

                branch.AddChannelCommand<ManagePauseCommand>("pause")
                    .WithDescription("Signal the running Migration Agent to checkpoint and pause.")
                    .WithExample("manage", "pause", "--job", "550e8400-e29b-41d4-a716-446655440000");

                branch.AddChannelCommand<ManageResumeCommand>("resume")
                    .WithDescription("Resume a paused job (re-queues it for Migration Agent pickup).")
                    .WithExample("manage", "resume", "--job", "550e8400-e29b-41d4-a716-446655440000");

                branch.AddChannelCommand<ManageCancelCommand>("cancel")
                    .WithDescription("Cancel a queued or running job.")
                    .WithExample("manage", "cancel", "--job", "550e8400-e29b-41d4-a716-446655440000");

                branch.AddChannelCommand<ManageLoginCommand>("login")
                    .WithDescription("Authenticate with a control plane endpoint and store the session token.")
                    .WithExample("manage", "login", "--url", "https://migration.example.com");

                branch.AddChannelCommand<ManageLogoutCommand>("logout")
                    .WithDescription("Revoke the stored session token for a control plane endpoint.")
                    .WithExample("manage", "logout", "--url", "https://migration.example.com");
            });

            // ── Discovery commands (run locally, never submit a MigrationJob) ────────
            config.AddBranch("discovery", branch =>
            {
                branch.SetDescription("Tools for finding out what we have and the implications of any migration.");

                branch.AddCommand<InventoryCommand>("inventory")
                    .WithDescription("Count work items and revisions per project. Results written to the Package path defined in the config.")
                    .WithExample("discovery", "inventory", "--config", "scenarios/inventory-ado-single-project.json");

                branch.AddCommand<DependencyCommand>("dependencies")
                    .WithDescription("Analyse work items for cross-project and cross-organisation links. Results written to the Package path defined in the config.")
                    .WithExample("discovery", "dependencies", "--config", "scenarios/discovery-dependency-ado-single-project.json");
            });

            // ── Control Plane management ─────────────────────────────────────────────
            config.AddBranch("controlplane", branch =>
            {
                branch.SetDescription("Manage the local Control Plane host process.");

                branch.AddCommand<ControlPlaneStartCommand>("start")
                    .WithDescription("Start the bundled Control Plane host in the current terminal. Only available in the packaged (zip) distribution.")
                    .WithExample("controlplane", "start");
            });

            // ── Terminal UI ───────────────────────────────────────────────────────────
            config.AddChannelCommand<TuiCommand>("tui")
                .WithDescription("Open the interactive Terminal UI showing live job state.")
                .WithExample("tui");

            // ── Configuration management ─────────────────────────────────────────
            config.AddBranch("config", branch =>
            {
                branch.SetDescription("Manage user preferences and create migration configuration files.");

                branch.AddCommand<ConfigNewCommand>("new")
                    .WithDescription("Interactive configuration wizard to create migration settings.")
                    .WithExample("config", "new")
                    .WithExample("config", "new", "--output", "my-migration.json");

                branch.AddCommand<ConfigSetCommand>("set")
                    .WithDescription("Set a user preference (e.g. scenario-folder).")
                    .WithExample("config", "set", "scenario-folder", "C:\\migrations\\configs");

                branch.AddCommand<ConfigGetCommand>("get")
                    .WithDescription("Read a user preference value.")
                    .WithExample("config", "get", "scenario-folder");
            });
        });

        return await app.RunAsync(args);
    }
}

