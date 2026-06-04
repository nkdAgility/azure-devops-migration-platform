# Quickstart Validation: Close DSL Migration Gaps

**Feature**: `specs/038-close-dsl-gaps/spec.md`
**Date**: 2026-06-03

---

## Prerequisites

- `dotnet` SDK 10.0+ installed
- Repository cloned and `dotnet restore` run from repo root
- All unit tests passing on `main` before branching: `dotnet test`

---

## Validation Scenarios

### Scenario Group 1 — Identity Resolution (GAP-001)

**What to run:**
```powershell
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests `
  --filter "FullyQualifiedName~IdentitiesOrchestrator"
```

**Expected outcomes:**
- Test `PrepareAsync_UpnMatch_ResolvesCorrectly` → PASS
- Test `PrepareAsync_DisplayNameMatch_ResolvesCorrectly` → PASS
- Test `PrepareAsync_AmbiguousDisplayName_LogsWarningAndFallsBack` → PASS
- Test `PrepareAsync_AdapterQueryFails_ContinuesAndLogsWarning` → PASS
- Test `Translate_WhenIsEnabledFalse_ReturnsSourceUnchanged` → PASS
- Test `Translate_AfterPrepare_ReturnsCachedResult` → PASS

**Simulated adapter smoke test:**
```powershell
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests `
  --filter "FullyQualifiedName~SimulatedIdentityAdapter"
```

---

### Scenario Group 2 — NodesModule skip guard (GAP-002, GAP-003)

**What to run:**
```powershell
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests `
  --filter "FullyQualifiedName~NodesModule"
```

**Expected outcomes:**
- Test `ImportAsync_WhenReplicateSourceTreeFalse_ReturnsSkipped` → PASS
- Test `ImportAsync_WhenEnabledFalse_ReturnsSkipped` → PASS
- Test `ImportAsync_WhenReplicateSourceTreeTrue_CallsOrchestrator` → PASS

**Verify INodeEnsurer is gone:**
```powershell
# Should return no results
Select-String -Path "src/**/*.cs" -Pattern "INodeEnsurer" -Recurse
```

---

### Scenario Group 3 — TranslatePath null return (GAP-005)

**What to run:**
```powershell
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests `
  --filter "FullyQualifiedName~TeamImportOrchestrator"
```

**Expected outcomes:**
- Test `TranslatePath_WhenTargetPathNull_ReturnsNull` → PASS
- Test `ImportTeamAsync_UntranslatableAreaPath_LogsWarningAndSkips` → PASS
- Test `ImportTeamAsync_UntranslatableIterationPath_LogsWarningAndSkips` → PASS

---

### Scenario Group 4 — Member identity skip (GAP-006)

**Expected outcomes:**
- Test `ImportTeamAsync_WhenMemberResolvesToDefault_SkipsAddAndLogsWarning` → PASS
- Test `ImportTeamAsync_DefaultTeam_LogsStructuredWarning` → PASS (GAP-004)

---

### Scenario Group 5 — GAP-007 scenario deletion

**Verify:**
```powershell
# Should return no results
Select-String -Path "features/**/*.feature" -Pattern "us1-write-idempotency" -Recurse
```

**Verify gap log entry:**
```powershell
Select-String -Path "analysis/dsl-gaps-detected.md" -Pattern "GAP-007"
# Must show "Status: RESOLVED"
```

---

### Scenario Group 6 — OTel in-memory exporter (GAP-008, GAP-009)

**What to run:**
```powershell
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests `
  --filter "FullyQualifiedName~ExportMetrics"
```

**Expected outcomes:**
- Test `ExportAsync_EmitsAttemptedCounter` — `migration.workitems.attempted` asserted via in-memory exporter → PASS
- Test `ExportAsync_EmitsRetriedCounter` — `migration.workitems.retried` asserted → PASS
- Test `ExportAsync_EmitsDurationHistogram` — `migration.workitem.duration.ms` histogram asserted → PASS
- Test `ExportAsync_MetricSnapshot_HistogramValuesMatch` — `RevisionCountMean` etc. asserted → PASS
- Test isolation: running all four in sequence produces independent counter values per test → PASS

---

### Full build and test gate

```powershell
dotnet clean
dotnet build --no-incremental
dotnet test
```

All three commands must succeed with zero warnings and zero failures before any task is declared complete.

---

### Gap log final check

```powershell
Select-String -Path "analysis/dsl-gaps-detected.md" -Pattern "Status: OPEN"
# Must return no results — all 9 gaps resolved
```
