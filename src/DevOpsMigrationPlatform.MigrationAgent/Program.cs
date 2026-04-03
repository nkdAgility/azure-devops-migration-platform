// Migration Agent — Worker Service
// Polls the control plane for jobs, executes them, and reports progress.
// Stateless: all durable state is written to the package via IArtefactStore/IStateStore.
// See docs/migration-agent.md.

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.MigrationAgent;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

// Register snapshot exporter + IMetricSnapshotStore + TelemetryOptions.
builder.Services.AddTelemetryServices(builder.Configuration);

// Register WellKnownMeterNames meters in the Aspire OTel pipeline.
// Do NOT reference WorkItemExportMetrics.MeterName (lives in the .NET 4.8 assembly).
builder.Services.AddOpenTelemetry()
    .WithMetrics(mb => mb
        .AddMeter(WellKnownMeterNames.WorkItemExport)
        .AddMeter(WellKnownMeterNames.AttachmentDownload));

// Singleton to carry the current lease id across services.
builder.Services.AddSingleton<ActiveLeaseState>();

// Named HttpClient for the Control Plane telemetry push.
var controlPlaneBaseUrl = new Uri(
    builder.Configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5100");
builder.Services.AddControlPlaneTelemetryClient(controlPlaneBaseUrl);

// Progress streaming to the Control Plane ring buffer.
builder.Services.AddControlPlaneProgressSink(controlPlaneBaseUrl);

// Composite sink fans out every ProgressEvent to all three sinks.
builder.Services.AddSingleton<IProgressSink>(sp => new CompositeProgressSink(
    sp.GetRequiredService<ILogger<CompositeProgressSink>>(),
    new AnsiProgressSink(),
    new PackageProgressSink(),
    sp.GetRequiredService<ControlPlaneProgressSink>()));

// Background timer that pushes MetricSnapshots to the Control Plane.
builder.Services.AddHostedService<ControlPlaneTelemetryTimer>();

builder.Services.AddHostedService<MigrationAgentWorker>();

var host = builder.Build();
host.Run();
