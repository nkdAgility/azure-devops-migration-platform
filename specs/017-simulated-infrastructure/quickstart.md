# Quickstart: Simulated Infrastructure Connector

**Feature**: 017-simulated-infrastructure

---

## Running a Simulated Export (Developer)

After this feature is implemented, run an offline export with no credentials:

1. **Open `.vscode/launch.json`** in VS Code and select the profile `queue-export-workitems-simulated-source`.
2. Press **F5** to run. The CLI submits a job using the `scenarios/queue-export-workitems-simulated-source.json` config.
3. Observe progress events in the terminal. The package is written to `output/<run>/WorkItems/`.
4. Inspect `output/<run>/WorkItems/` — revision folders follow the `yyyy-MM-dd/<ticks>-<id>-<rev>/` layout with `revision.json` in each.

No Azure DevOps organisation, PAT, or network connection is needed.

---

## Running a Simulated Import

1. Open `.vscode/launch.json` and select `queue-import-workitems-simulated-fixture`.
2. Press **F5**. The CLI runs an import against the bundled test fixture at `scenarios/testdata/workitems-2items-flat.zip`.
3. Alternatively, run the new `queue-import-workitems-simulated-target` profile to import the package produced by the simulated export.

---

## Adding a New Connector

To add a new connector (e.g. GitHub) in the future:

1. Create `src/DevOpsMigrationPlatform.Infrastructure.GitHub/`.
2. Add `GitHubEndpointOptions : MigrationEndpointOptions` in the new assembly.
3. In `AddGitHubWorkItemExport()`:
   ```csharp
   services.AddEndpointOptionsType("GitHub", typeof(GitHubEndpointOptions));
   ```
4. Implement `GitHubWorkItemRevisionSourceFactory.CreateAsync(MigrationEndpointOptions endpoint, CancellationToken ct)` — cast to `GitHubEndpointOptions` internally.
5. Write a scenario config with `"Source": { "Type": "GitHub", ... }`.
6. Add a `.vscode/launch.json` profile.

**Zero changes** to `Abstractions`, `Infrastructure`, `Infrastructure.AzureDevOps`, `Infrastructure.TfsObjectModel`, or `Infrastructure.Simulated`.

---

## Running All Tests

```powershell
dotnet clean && dotnet build --no-incremental
dotnet test
```

To run only the Simulated assembly tests:
```powershell
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests
```

To run the simulated export system test:
```powershell
dotnet test --filter "TestCategory=SystemTest&FullyQualifiedName~SimulatedExport"
```
