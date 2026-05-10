// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

// Migration Agent — Worker Service
// Polls the control plane for jobs, executes them, and reports progress.
// Stateless: all durable state is written to the package via IArtefactStore/IStateStore.
// See docs/migration-agent.md.

using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.MigrationAgent;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults(DevOpsMigrationPlatform.Abstractions.WellKnownServiceNames.MigrationAgent);

// Filter customer-identifiable log data from the OTel pipeline (Azure Monitor).
builder.Logging.AddDataClassificationFilter();

var controlPlaneBaseUrl = new Uri(
    builder.Configuration["ControlPlane:BaseUrl"]
    ?? builder.Configuration["MigrationPlatform:Environment:ControlPlane:BaseUrl"]
    ?? "http://localhost:5100");

// All agent service registrations are in MigrationAgentServiceExtensions so that
// LocalStackHost (CLI in-process mode) can use the exact same registrations.
builder.AddMigrationAgentServices(controlPlaneBaseUrl);

var host = builder.Build();
host.Run();
