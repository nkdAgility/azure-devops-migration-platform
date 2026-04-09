using DevOpsMigrationPlatform.CLI.Commands;
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
        AnsiConsole.Write(new FigletText("DevOps Migration").LeftJustified().Color(Color.Blue));
        AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("devopsmigration");
#if DEBUG
            config.PropagateExceptions();
            config.ValidateExamples();
#endif

            // ── Migration commands (all read their configuration from --config) ──────
            config.AddChannelCommand<MigrationPrepareCommand>("prepare")
                .WithDescription("Validate config, compute configHash, print planned modules. No job is submitted.")
                .WithExample("prepare", "--config", "migration.json");

            config.AddChannelCommand<MigrationExportCommand>("export")
                .WithDescription("Submit an export-only job. Source type (AzureDevOpsServices or TeamFoundationServer) is read from the config file.")
                .WithExample("export", "--config", "migration.json")
                .WithExample("export", "--config", "scenarios/export-ado-workitems-single-project.json");

            config.AddChannelCommand<MigrationImportCommand>("import")
                .WithDescription("Submit an import-only job. Reads the package from artefacts.path in the config file.")
                .WithExample("import", "--config", "migration.json");

            config.AddChannelCommand<MigrationValidateCommand>("validate")
                .WithDescription("Run pre-flight validation on an existing package.")
                .WithExample("validate", "--config", "migration.json");

            config.AddChannelCommand<MigrationMigrateCommand>("migrate")
                .WithDescription("Full lifecycle: export → validate → import in one orchestrated run.")
                .WithExample("migrate", "--config", "migration.json");

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
                    .WithDescription("Count work items and revisions per project. Results written to discovery-summary.csv.")
                    .WithExample("discovery", "inventory", "--config", "migration.json")
                    .WithExample("discovery", "inventory", "--config", "migration.json", "--output", "./inventory-results");
            });

            // ── Terminal UI ───────────────────────────────────────────────────────────
            config.AddChannelCommand<TuiCommand>("tui")
                .WithDescription("Open the interactive Terminal UI showing live job state.")
                .WithExample("tui");

            // ── Developer convenience (not in canonical CLI spec, retained for ease) ──
            config.AddCommand<ConfigureCommand>("configure")
                .WithDescription("Interactive configuration wizard to create migration settings.")
                .WithExample("configure")
                .WithExample("configure", "--output", "my-migration.json");
        });

        return await app.RunAsync(args);
    }
}

