// Migration Agent — Worker Service
// Polls the control plane for jobs, executes them, and reports progress.
// Stateless: all durable state is written to the package via IArtefactStore/IStateStore.
// See docs/migration-agent.md.

using DevOpsMigrationPlatform.MigrationAgent;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddHostedService<MigrationAgentWorker>();

var host = builder.Build();
host.Run();
