# Quickstart — IdentitiesModule, NodeStructureModule & TeamsModule

**Phase 1 Output** — getting started guide for implementers.

---

## Prerequisites

1. **.NET 10 SDK** installed (see `global.json`)
2. Solution builds clean: `dotnet clean && dotnet build --no-incremental`
3. All existing tests pass: `dotnet test`
4. Feature branch `024-teams-module` checked out

---

## Implementation Order

Follow the phases in `tasks.md`:

1. **Phase 0** — Research API surfaces (T000a–T000g). Populate `research.md`.
2. **Phase 1** — Create options classes and interface stubs (T001–T006). These are all independent and can be done in parallel.
3. **Phase 2** — Prerequisite refactoring (T007–T012). Extract interfaces from existing code. All existing tests must pass after each step.
4. **Phase 3** — IdentitiesModule (T013–T026). Start with Gherkin feature files, then implement.
5. **Phase 3b** — Prepare phase (T024b–T024g). Implements target discovery and validation.
6. **Phase 4–12** — Remaining user stories per tasks.md ordering.

---

## Key Patterns

### Module Structure

Every module follows the same pattern:

```csharp
public sealed class ExampleModule : IModule
{
    public string Name => "Example";

    public async Task ExportAsync(MigrationJob job, IProgressSink progress, CancellationToken ct)
    {
        // Stream from IExampleSource → write to IArtefactStore
    }

    public async Task ImportAsync(MigrationJob job, IProgressSink progress, CancellationToken ct)
    {
        // Read from IArtefactStore → write to IExampleTarget
    }

    public async Task<ValidationResult> ValidateAsync(MigrationJob job, CancellationToken ct)
    {
        // Verify package artefacts exist and are well-formed
    }
}
```

### Extension Pattern (TeamsModule)

Extensions are hardcoded in the module's orchestrator:

```csharp
// TeamExportOrchestrator.cs
await ExportTeamSettingsAsync(team, ct);        // 1. Settings
await ExportNodeStructureAsync(team, ct);       // 2. NodeStructure (records paths)
await ExportTeamIterationsAsync(team, ct);      // 3. Iterations
await ExportTeamMembersAsync(team, ct);         // 4. Members
await ExportTeamCapacityAsync(team, ct);        // 5. Capacity
```

### Connector Registration

Each connector registers via DI:

```csharp
// Simulated
services.AddSimulatedIdentityServices();
services.AddSimulatedTeamServices();

// Azure DevOps
services.AddAzureDevOpsIdentityServices();
services.AddAzureDevOpsTeamServices();

// TFS (subprocess bridge)
services.AddTfsIdentityServices();
services.AddTfsTeamServices();
```

---

## Verification

After each phase checkpoint:

1. `dotnet clean && dotnet build --no-incremental` — MUST pass
2. `dotnet test` — ALL tests MUST pass
3. At Phase 10+: Run a scenario config via `.vscode/launch.json` debug profile

---

## Key Files

| File | Purpose |
|------|---------|
| `specs/024-teams-module/spec.md` | Feature specification |
| `specs/024-teams-module/plan.md` | Implementation plan |
| `specs/024-teams-module/tasks.md` | Task breakdown |
| `specs/024-teams-module/data-model.md` | JSON schemas |
| `specs/024-teams-module/contracts/interfaces.md` | Interface signatures |
| `specs/024-teams-module/research.md` | API research findings |
