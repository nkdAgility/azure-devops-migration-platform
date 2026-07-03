# Module Config Contract Clean-Break Migration (MC-H1 + MC-H2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the module-anatomy contract's mandated `IModuleContract` surface and restructure all four modules' options into the three canonical aspects (`Selection` / `Data` / `Processing`), as a single hard cutover to `ConfigVersion` "2.0" with no legacy shim and precise, actionable validation errors for v1 configs.

**Architecture:** New contract interfaces (`IModuleContract`, `ISelectionDefinition`, `IDataDefinition`, `IProcessingDefinition`) live in `DevOpsMigrationPlatform.Abstractions.Agent/Modules` next to `IModule`, which gains a `Contract` property implemented by all four modules. All module options classes are restructured into `Selection`/`Data`/`Processing` sub-objects in place (same assemblies, same section names), and a v1-shape detector in `ConfigurationService` plus a `ConfigVersion == "2.0"` check in `MigrationPlatformOptionsValidator` fail-fast with rewrite instructions. `migration.schema.json` is regenerated from the new types; every scenario config, test fixture, wizard output, and doc is migrated in the same change, recorded by ADR 0028 (Class C consent).

**Tech Stack:** .NET 9 (net481 multi-target for Abstractions/Abstractions.Agent via `#if NET7_0_OR_GREATER`), Microsoft.Extensions.Options (`IValidateOptions`, `ValidateOnStart`), System.Text.Json, NJsonSchema (SchemaGenerator), MSTest.

---

## Operator ruling (recorded)

MC-H1 and MC-H2 are executed together as ONE clean break:

- **No legacy config support. No deprecation shim.** MC-H2's triage note "optionally keep legacy keys behind a deprecation shim" is explicitly rejected.
- Hard cutover: `MigrationPlatform.ConfigVersion` bumps `"1.0"` → `"2.0"`.
- v1 configs must fail with a precise validation error telling the user exactly how to rewrite the file — never a silent-binding mystery.
- Both items are change-class **C** (`analysis/archcheck/triage.json`), requiring: explicit operator consent (this ruling), ADR in the same change (Task 14), contract compatibility tests, and a RED → GREEN → REFACTOR trace (every task below).

## Contract text being implemented

From `.agents/10-contracts/specs/module-anatomy-contract.md`:

> ## Contract Surface
> - `IModule.Contract`
> - `IModuleContract`
> - `ISelectionDefinition`
> - `IDataDefinition`
> - `IProcessingDefinition`
>
> ## Required Semantics
> 1. Module config uses exactly three top-level aspects: `Selection`, `Data`, `Processing`
> 2. `Scope` and `Extensions` are legacy and must not be used for new module designs.
> 3. Contract metadata is platform-owned and not user-editable.
> 4. Required entries cannot be disabled; optional entries may be enabled/disabled.
> 5. Connector capability gaps are connector concerns, not anatomy taxonomy changes.
> 6. Processing entries describe runtime behavior and are not package data kinds.
> 7. Capability seams consumed by processing entries (for example `FieldTransform`, `NodeTranslation`, `IdentityLookup`) must remain singular and canonical.
>
> ## Canonical Aspect Responsibilities
> - `Selection`: in-scope entity selection
> - `Data`: canonical package payload for selected entities
> - `Processing`: runtime behavior policies for export/import phases

## Aspect mapping (nothing lost in translation)

Derived from today's options classes — every existing property has a v2 home:

| Module | v1 property | v2 home |
|---|---|---|
| WorkItems | `Scope.Query` | `Selection.Query` |
| WorkItems | `Scope.Filters` (`List<WorkItemFilterOptions>`) | `Selection.Filters` |
| WorkItems | `Extensions.Revisions` (`EnabledExtensionOptions`) | `Data.Revisions` |
| WorkItems | `Extensions.Comments` (`CommentsExtensionOptionsConfig`: `Enabled`, `IncludeDeleted`) | `Data.Comments` |
| WorkItems | `Extensions.EmbeddedImages` (`EmbeddedImagesExtensionOptionsConfig`: `Enabled`, `DownloadTimeoutSeconds`) | `Data.EmbeddedImages` |
| WorkItems | `Extensions.WorkItemResolutionStrategy` (`Enabled`, `Strategy`, `FieldName`, `UrlPattern`) | `Processing.WorkItemResolutionStrategy` (runtime import behaviour, not package data) |
| Teams | `Scope` (`"all"`/`"teams"`) | `Selection.Scope` |
| Teams | `Filter` (regex) | `Selection.Filter` |
| Teams | `AlwaysExport` | `Processing.AlwaysExport` (re-run/resume runtime policy) |
| Teams | `Extensions.TeamSettings` | `Data.TeamSettings` |
| Teams | `Extensions.TeamIterations` | `Data.TeamIterations` |
| Teams | `Extensions.TeamMembers` | `Data.TeamMembers` |
| Teams | `Extensions.TeamCapacity` | `Data.TeamCapacity` |
| Teams | `Extensions.NodeTranslation` | `Processing.NodeTranslation` (capability seam use, semantics rule 7) |
| Teams | `Extensions.IdentityLookup` | `Processing.IdentityLookup` (capability seam use, semantics rule 7) |
| Nodes | `ReplicateSourceTree` | `Processing.ReplicateSourceTree` (import-phase behaviour) |
| Identities | `DefaultIdentity` | `Processing.DefaultIdentity` (resolution fallback behaviour) |
| All | `Enabled` | stays top-level (module participation, not an aspect) |

Work-item Links and Attachments remain intrinsic core behaviour (not configurable) — unchanged.

## File map

**Created (production):**
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModuleContract.cs` — `IModuleContract`, `ISelectionDefinition`, `IDataDefinition`, `IProcessingDefinition`, plus sealed record implementations `SelectionDefinition`, `DataDefinition`, `ProcessingDefinition`, `ModuleContract`
- `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsSelectionOptions.cs`
- `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsDataOptions.cs`
- `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsProcessingOptions.cs`
- `docs/adr/0028-module-anatomy-selection-data-processing-config.md`

**Modified (production):**
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs` — add `IModuleContract Contract { get; }`
- `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsModuleOptions.cs` — `Scope`/`Extensions` → `Selection`/`Data`/`Processing`
- `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsScopeOptions.cs` — **deleted** (replaced by `WorkItemsSelectionOptions`)
- `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsExtensionsOptions.cs` — **deleted** (replaced by Data/Processing options)
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/WorkItemsModuleExtensions.cs` — `FromOptions` reads new shape
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/TeamsModuleOptions.cs` — restructure + new `TeamsSelectionOptions`/`TeamsDataOptions`/`TeamsProcessingOptions` (same file, follows existing multi-class pattern)
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/NodesModuleOptions.cs` — add `NodesProcessingOptions`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IdentitiesModuleOptions.cs` — add `IdentitiesProcessingOptions`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleBase.cs` — abstract `Contract` property
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`, `TeamsModule.cs`, `NodesModule.cs`, `IdentitiesModule.cs` — implement `Contract`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsOrchestrator.cs` — `options.Scope`→`options.Selection.Scope`, `options.Filter`→`options.Selection.Filter`, `options.AlwaysExport`→`options.Processing.AlwaysExport`, `options.Extensions`→pass `options.Data`+`options.Processing` (lines ~595–818)
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs` — `_options.ReplicateSourceTree`→`_options.Processing.ReplicateSourceTree` (lines ~183, 191)
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/IdentityMappingService.cs` — `_options.DefaultIdentity`→`_options.Processing.DefaultIdentity` (lines ~84–85)
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityTranslation/IdentityTranslationTool.cs` — same rename (lines ~39, 145–148)
- `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformOptionsValidator.cs` — `ConfigVersion == "2.0"` gate
- `src/DevOpsMigrationPlatform.Infrastructure/Config/ConfigurationService.cs` — v1-shape detection with actionable error (Load, line ~95); `SaveConfigurationAsync` writes `"2.0"` (line ~206)
- `src/DevOpsMigrationPlatform.Abstractions/Options/MigrationPlatformOptions.cs` — XML-doc example → v2 shape
- `src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json` — regenerated
- `src/DevOpsMigrationPlatform.CLI.Migration/appsettings.json` — `ConfigVersion` → `"2.0"`

**Modified (scenario/sample configs — the enumerated blast radius, 16 JSON files):**
- `scenarios/SystemTest-Simulated-Export-WorkItems.json`
- `scenarios/SystemTest-Simulated-Import-WorkItems.json`
- `scenarios/SystemTest-Simulated-Import-WorkItems-Fixture.json`
- `scenarios/SystemTest-Simulated-Import-WorkItems-SingleProject.json`
- `scenarios/SystemTest-Simulated-Migrate-Roundtrip.json`
- `scenarios/SystemTest-Live-Migrate-AzureDevOps-Complete.json`
- `scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-SingleProject.json`
- `scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-InlineComments.json`
- `scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-NoFilterScopes.json`
- `scenarios/SystemTest-Live-Export-TFS-WorkItems-SingleProject.json`
- `scenarios/SystemTest-Live-Import-AzureDevOps-WorkItems-Fixture.json`
- `scenarios/SystemTest-Live-Import-AzureDevOps-WorkItems-Fixture-FieldMissing.json`
- `scenarios/SystemTest-Live-Import-TFS-WorkItems-Fixture.json`
- `scenarios/manual/export.json`
- `scenarios/manual/import.json`
- `scenarios/client/migration.json`

**Modified (test fixtures/builders):**
- `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Cli/TfsExport/TfsExportBuilder.cs` — `BuildConfigJson()` emits v2 (`ConfigVersion "2.0"`, `Data` instead of `Extensions`)
- `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/QueueCommandTests.cs` — inline config JSON → v2
- `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Platform/MigrationPlatformOptionsDeserializationTests.cs` — assert new shape
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleImportTests.cs` — construct new options shape
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/WorkItemOrchestratorFilterTests.cs` — construct new options shape
- Any further hits from the sweep greps in Task 11/12 (`"ConfigVersion": "1.0"`, `"Scope"`, `"Extensions"`, `WorkItemsScopeOptions`, `WorkItemsExtensionsOptions` under `tests/`)

**Created (tests):**
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/ModuleContractTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Config/ConfigVersionGateTests.cs`

**Modified (docs):**
- `docs/configuration-reference.md`, `docs/operator-guide.md`, `docs/capabilities-guide.md` — v1 `Scope`/`Extensions` examples → v2
- `docs/modules.md` — document `IModule.Contract` and the three aspects
- `specs/**` files that mention the old shape are historical spec records and are **not** modified.

**Connector coverage note (explicit):** module options are connector-neutral — Simulated, AzureDevOpsServices, and TeamFoundationServer connectors all bind the very same `MigrationPlatform:Modules:*` sections; there are no connector-specific module-option touchpoints in code. Connector-specific touchpoints are limited to fixtures: the scenario list above spans all three connectors (Simulated-, Live-AzureDevOps-, Live-TFS- files) and `TfsExportBuilder` covers the TFS + Simulated CLI paths. `Abstractions`/`Abstractions.Agent` multi-target net481 for the TFS agent — every new options class must keep the existing `#if NET7_0_OR_GREATER … IConfigSection` pattern.

---

## Config migration story

### v1 (before) — representative WorkItems export config

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "1.0",
    "Mode": "Export",
    "Source": { "Type": "AzureDevOpsServices", "Url": "https://dev.azure.com/nkdagility", "Project": "MyProject" },
    "Package": { "WorkingDirectory": "D:\\exports\\run-001", "CreatePackage": true },
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Scope": {
          "Query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]",
          "Filters": [
            { "Mode": "Include", "Field": "System.WorkItemType", "Pattern": "^(Bug|User Story)$" }
          ]
        },
        "Extensions": {
          "Revisions": { "Enabled": true },
          "Comments": { "Enabled": true, "IncludeDeleted": false },
          "EmbeddedImages": { "Enabled": true, "DownloadTimeoutSeconds": 30 },
          "WorkItemResolutionStrategy": { "Enabled": true, "Strategy": "TargetField", "FieldName": "Custom.ReflectedWorkItemId" }
        }
      }
    }
  }
}
```

### v2 (after) — same intent, new anatomy

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "2.0",
    "Mode": "Export",
    "Source": { "Type": "AzureDevOpsServices", "Url": "https://dev.azure.com/nkdagility", "Project": "MyProject" },
    "Package": { "WorkingDirectory": "D:\\exports\\run-001", "CreatePackage": true },
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Selection": {
          "Query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]",
          "Filters": [
            { "Mode": "Include", "Field": "System.WorkItemType", "Pattern": "^(Bug|User Story)$" }
          ]
        },
        "Data": {
          "Revisions": { "Enabled": true },
          "Comments": { "Enabled": true, "IncludeDeleted": false },
          "EmbeddedImages": { "Enabled": true, "DownloadTimeoutSeconds": 30 }
        },
        "Processing": {
          "WorkItemResolutionStrategy": { "Enabled": true, "Strategy": "TargetField", "FieldName": "Custom.ReflectedWorkItemId" }
        }
      }
    }
  }
}
```

### Exact error text a v1 user sees

Emitted by `ConfigurationService.LoadConfigurationAsync` (and equivalently by the `MigrationPlatformOptionsValidator` `ValidateOnStart` path) as an `InvalidOperationException` / `OptionsValidationException`:

```
Configuration error in '<path>': this file uses configuration version '1.0', which is no longer supported. This release requires ConfigVersion '2.0'.
Module options are now expressed as three aspects: 'Selection' (what to migrate), 'Data' (what to carry), 'Processing' (how to execute).
To upgrade 'Modules.WorkItems':
  1. Rename 'Scope' to 'Selection' ('Query' and 'Filters' are unchanged).
  2. Move 'Extensions.Revisions', 'Extensions.Comments', and 'Extensions.EmbeddedImages' under 'Data'.
  3. Move 'Extensions.WorkItemResolutionStrategy' under 'Processing'.
  4. Delete the now-empty 'Extensions' object.
  5. Set 'MigrationPlatform.ConfigVersion' to '2.0'.
See docs/configuration-reference.md ('Module configuration anatomy') for the full v2 layout.
```

If `ConfigVersion` already says `"2.0"` but stray v1 keys remain, the error names them precisely:

```
Configuration error in '<path>': 'Modules.WorkItems' contains legacy key(s) 'Scope', 'Extensions' which were removed in ConfigVersion 2.0. Rename 'Scope' to 'Selection'; move 'Extensions.Revisions'/'Comments'/'EmbeddedImages' under 'Data' and 'Extensions.WorkItemResolutionStrategy' under 'Processing'. See docs/configuration-reference.md.
```

---

## Tasks

Run all tests from the repo root: `C:\Users\MartinHinshelwoodNKD\source\repos\azure-devops-migration-platform`.

### Task 1: Contract surface — `IModuleContract` and aspect definitions

**Files:**
- Create: `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModuleContract.cs`
- Test: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/ModuleContractTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public class ModuleContractTests
{
    [TestMethod]
    [TestCategory("L0")]
    public void ModuleContract_ExposesThreeAspectCollections()
    {
        var contract = new ModuleContract(
            moduleName: "WorkItems",
            selection: [new SelectionDefinition("Query", Required: true), new SelectionDefinition("Filters", Required: false)],
            data: [new DataDefinition("Revisions", Required: false)],
            processing: [new ProcessingDefinition("WorkItemResolutionStrategy", Required: false)]);

        Assert.AreEqual("WorkItems", contract.ModuleName);
        Assert.AreEqual(2, contract.Selection.Count);
        Assert.AreEqual(1, contract.Data.Count);
        Assert.AreEqual(1, contract.Processing.Count);
        Assert.IsTrue(contract.Selection[0].Required);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~ModuleContractTests" 2>&1 | tail -20`
Expected: FAIL — compile error `The type or namespace name 'ModuleContract' could not be found`.

- [ ] **Step 3: Write minimal implementation**

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// A single Selection aspect entry — what this module can bring into scope.
/// See .agents/10-contracts/specs/module-anatomy-contract.md.
/// </summary>
public interface ISelectionDefinition
{
    /// <summary>Entry name as it appears under the module's <c>Selection</c> config object.</summary>
    string Name { get; }

    /// <summary>Required entries cannot be disabled; optional entries may be enabled/disabled.</summary>
    bool Required { get; }
}

/// <summary>A single Data aspect entry — a canonical package payload kind for selected entities.</summary>
public interface IDataDefinition
{
    /// <summary>Entry name as it appears under the module's <c>Data</c> config object.</summary>
    string Name { get; }

    /// <summary>Required entries cannot be disabled; optional entries may be enabled/disabled.</summary>
    bool Required { get; }
}

/// <summary>A single Processing aspect entry — a runtime behaviour policy for export/import phases.</summary>
public interface IProcessingDefinition
{
    /// <summary>Entry name as it appears under the module's <c>Processing</c> config object.</summary>
    string Name { get; }

    /// <summary>Required entries cannot be disabled; optional entries may be enabled/disabled.</summary>
    bool Required { get; }
}

/// <summary>
/// Platform-owned, non-user-editable metadata describing a module's configuration anatomy:
/// exactly three aspects — Selection (what to migrate), Data (what to carry),
/// Processing (how to execute). Exposed via <see cref="IModule.Contract"/>.
/// </summary>
public interface IModuleContract
{
    /// <summary>Module name matching its <c>MigrationPlatform:Modules:{Name}</c> config section.</summary>
    string ModuleName { get; }

    /// <summary>In-scope entity selection entries.</summary>
    IReadOnlyList<ISelectionDefinition> Selection { get; }

    /// <summary>Canonical package payload entries for selected entities.</summary>
    IReadOnlyList<IDataDefinition> Data { get; }

    /// <summary>Runtime behaviour policy entries for export/import phases.</summary>
    IReadOnlyList<IProcessingDefinition> Processing { get; }
}

/// <summary>Immutable Selection entry.</summary>
public sealed record SelectionDefinition(string Name, bool Required) : ISelectionDefinition;

/// <summary>Immutable Data entry.</summary>
public sealed record DataDefinition(string Name, bool Required) : IDataDefinition;

/// <summary>Immutable Processing entry.</summary>
public sealed record ProcessingDefinition(string Name, bool Required) : IProcessingDefinition;

/// <summary>Immutable <see cref="IModuleContract"/> implementation used by all platform modules.</summary>
public sealed class ModuleContract(
    string moduleName,
    IReadOnlyList<ISelectionDefinition> selection,
    IReadOnlyList<IDataDefinition> data,
    IReadOnlyList<IProcessingDefinition> processing) : IModuleContract
{
    public string ModuleName { get; } = moduleName;
    public IReadOnlyList<ISelectionDefinition> Selection { get; } = selection;
    public IReadOnlyList<IDataDefinition> Data { get; } = data;
    public IReadOnlyList<IProcessingDefinition> Processing { get; } = processing;
}
```

Note: `Abstractions.Agent` multi-targets net481 — records with primary constructors require C# 9+/LangVersion latest, which this repo already uses (see existing `#if NET7_0_OR_GREATER` files). If net481 compilation complains about `IsExternalInit`, replace the records with sealed classes using get-only properties and constructor assignment, mirroring `ModuleContract` above.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~ModuleContractTests" 2>&1 | tail -5`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModuleContract.cs tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/ModuleContractTests.cs
git commit -m "feat(contracts): add IModuleContract with Selection/Data/Processing definitions (MC-H1)"
```

### Task 2: Widen `IModule` with `Contract`; implement on `WorkItemsModule` and `ModuleBase`

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs` (after `SupportsValidate`, ~line 65)
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleBase.cs`
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
- Test: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/ModuleContractTests.cs`

- [ ] **Step 1: Write the failing test** (append to `ModuleContractTests`)

```csharp
    [TestMethod]
    [TestCategory("L0")]
    public void AllModules_ExposeContract_WithMatchingModuleName()
    {
        // Resolve concrete modules the same way existing module tests in this
        // project construct them (reuse that project's existing module test
        // factory/builder). Assert against the IModule seam:
        foreach (var module in ModuleContractTestData.CreateAllModules())
        {
            IModuleContract contract = module.Contract;
            Assert.IsNotNull(contract, $"{module.GetType().Name} must expose a Contract");
            Assert.IsFalse(string.IsNullOrWhiteSpace(contract.ModuleName));
            Assert.IsNotNull(contract.Selection);
            Assert.IsNotNull(contract.Data);
            Assert.IsNotNull(contract.Processing);
        }
    }
```

Add `ModuleContractTestData.CreateAllModules()` as a static helper in the same file that instantiates `WorkItemsModule`, `TeamsModule`, `NodesModule`, `IdentitiesModule` reusing the constructor arrangements already present in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleImportTests.cs` and the Teams/Nodes/Identities module test files in the same folder (copy their existing fake/mock constructor arguments verbatim — do not invent new fakes). For this task, have it return only `WorkItemsModule`; Task 3 extends it to all four.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~ModuleContractTests" 2>&1 | tail -20`
Expected: FAIL — `'IModule' does not contain a definition for 'Contract'`.

- [ ] **Step 3: Implement**

In `IModule.cs`, after the `SupportsValidate` property:

```csharp
    /// <summary>
    /// Platform-owned configuration anatomy metadata for this module:
    /// Selection (what to migrate), Data (what to carry), Processing (how to execute).
    /// Not user-editable. See .agents/10-contracts/specs/module-anatomy-contract.md
    /// and ADR 0028.
    /// </summary>
    IModuleContract Contract { get; }
```

In `ModuleBase.cs` add:

```csharp
    /// <inheritdoc cref="IModule.Contract"/>
    public abstract IModuleContract Contract { get; }
```

In `WorkItemsModule.cs` add:

```csharp
    private static readonly IModuleContract WorkItemsContract = new ModuleContract(
        moduleName: "WorkItems",
        selection:
        [
            new SelectionDefinition("Query", Required: true),
            new SelectionDefinition("Filters", Required: false)
        ],
        data:
        [
            new DataDefinition("Revisions", Required: false),
            new DataDefinition("Comments", Required: false),
            new DataDefinition("EmbeddedImages", Required: false),
            new DataDefinition("Links", Required: true),        // intrinsic — cannot be disabled
            new DataDefinition("Attachments", Required: true)   // intrinsic — cannot be disabled
        ],
        processing:
        [
            new ProcessingDefinition("WorkItemResolutionStrategy", Required: false)
        ]);

    public IModuleContract Contract => WorkItemsContract;
```

(`WorkItemsModule` implements `IModule` directly, not `ModuleBase`, so the property is declared directly. Any other direct `IModule` implementations found by `grep ": IModule" src tests` — including test doubles — get the same treatment with their own names.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~ModuleContractTests" 2>&1 | tail -5`
Expected: PASS. Then run `dotnet build DevOpsMigrationPlatform.sln 2>&1 | tail -5` — any remaining `IModule` implementers that fail to compile (fakes in tests) get a minimal `public IModuleContract Contract => new ModuleContract("Fake", [], [], []);`.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(contracts): widen IModule with Contract; WorkItemsModule anatomy (MC-H1)"
```

### Task 3: Contracts for Teams, Nodes, Identities modules

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs`, `NodesModule.cs`, `IdentitiesModule.cs`
- Test: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/ModuleContractTests.cs`

- [ ] **Step 1: Extend the failing test** — extend `ModuleContractTestData.CreateAllModules()` to return all four modules, and add:

```csharp
    [TestMethod]
    [TestCategory("L0")]
    public void ModuleContracts_HaveExpectedAnatomy()
    {
        var byName = ModuleContractTestData.CreateAllModules()
            .ToDictionary(m => m.Contract.ModuleName, m => m.Contract);

        CollectionAssert.AreEquivalent(
            new[] { "WorkItems", "Teams", "Nodes", "Identities" }, byName.Keys.ToArray());

        var teams = byName["Teams"];
        CollectionAssert.AreEquivalent(new[] { "Scope", "Filter" }, teams.Selection.Select(s => s.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "TeamSettings", "TeamIterations", "TeamMembers", "TeamCapacity" },
            teams.Data.Select(d => d.Name).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { "AlwaysExport", "NodeTranslation", "IdentityLookup" },
            teams.Processing.Select(p => p.Name).ToArray());

        CollectionAssert.AreEquivalent(new[] { "ReplicateSourceTree" }, byName["Nodes"].Processing.Select(p => p.Name).ToArray());
        CollectionAssert.AreEquivalent(new[] { "DefaultIdentity" }, byName["Identities"].Processing.Select(p => p.Name).ToArray());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~ModuleContractTests" 2>&1 | tail -20`
Expected: FAIL (Teams/Nodes/Identities have no meaningful contract yet or compile error).

- [ ] **Step 3: Implement** — in each module class:

`TeamsModule.cs`:

```csharp
    private static readonly IModuleContract TeamsContract = new ModuleContract(
        moduleName: "Teams",
        selection:
        [
            new SelectionDefinition("Scope", Required: true),
            new SelectionDefinition("Filter", Required: false)
        ],
        data:
        [
            new DataDefinition("TeamSettings", Required: false),
            new DataDefinition("TeamIterations", Required: false),
            new DataDefinition("TeamMembers", Required: false),
            new DataDefinition("TeamCapacity", Required: false)
        ],
        processing:
        [
            new ProcessingDefinition("AlwaysExport", Required: false),
            new ProcessingDefinition("NodeTranslation", Required: false),
            new ProcessingDefinition("IdentityLookup", Required: false)
        ]);

    public IModuleContract Contract => TeamsContract;
```

`NodesModule.cs`:

```csharp
    private static readonly IModuleContract NodesContract = new ModuleContract(
        moduleName: "Nodes",
        selection: [],
        data: [new DataDefinition("ClassificationNodes", Required: true)],
        processing: [new ProcessingDefinition("ReplicateSourceTree", Required: false)]);

    public IModuleContract Contract => NodesContract;
```

`IdentitiesModule.cs`:

```csharp
    private static readonly IModuleContract IdentitiesContract = new ModuleContract(
        moduleName: "Identities",
        selection: [],
        data: [new DataDefinition("Identities", Required: true)],
        processing: [new ProcessingDefinition("DefaultIdentity", Required: false)]);

    public IModuleContract Contract => IdentitiesContract;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~ModuleContractTests" 2>&1 | tail -5`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(contracts): Teams/Nodes/Identities module contracts (MC-H1)"
```

### Task 4: New WorkItems options anatomy (`Selection`/`Data`/`Processing`)

**Files:**
- Create: `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsSelectionOptions.cs`, `WorkItemsDataOptions.cs`, `WorkItemsProcessingOptions.cs`
- Modify: `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsModuleOptions.cs`
- Delete: `src/DevOpsMigrationPlatform.Abstractions/Options/WorkItemsScopeOptions.cs`, `WorkItemsExtensionsOptions.cs`
- Test: `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Platform/MigrationPlatformOptionsDeserializationTests.cs`

- [ ] **Step 1: Write the failing test** — update `MigrationPlatformOptionsDeserializationTests` (lines ~78–117): replace the v1 JSON blocks with the v2 JSON from the "Config migration story" section above and assert:

```csharp
    Assert.AreEqual("SELECT [System.Id] FROM WorkItems", wi.Selection.Query);
    Assert.IsTrue(wi.Data.Revisions.Enabled);
    Assert.IsTrue(wi.Data.Comments.Enabled);
    Assert.IsTrue(wi.Data.Comments.IncludeDeleted);
    Assert.IsTrue(wi.Data.EmbeddedImages.Enabled);
    Assert.AreEqual(45, wi.Data.EmbeddedImages.DownloadTimeoutSeconds);
    Assert.AreEqual("TargetField", wi.Processing.WorkItemResolutionStrategy.Strategy);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Tests --filter "FullyQualifiedName~MigrationPlatformOptionsDeserializationTests" 2>&1 | tail -20`
Expected: FAIL — `'WorkItemsModuleOptions' does not contain a definition for 'Selection'`.

- [ ] **Step 3: Implement**

`WorkItemsSelectionOptions.cs` (content of old `WorkItemsScopeOptions` renamed):

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Selection aspect for the WorkItems module — what to migrate.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Selection</c>.
/// </summary>
public sealed class WorkItemsSelectionOptions
{
    /// <summary>
    /// WIQL query selecting work items to operate on.
    /// Default: <c>SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]</c>.
    /// </summary>
    public string Query { get; init; } = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";

    /// <summary>
    /// Optional field-level filters. Each filter specifies a field, mode (include/exclude),
    /// and a regex pattern. Multiple filters are combined with AND logic.
    /// </summary>
    public List<WorkItemFilterOptions> Filters { get; init; } = new();
}
```

`WorkItemsDataOptions.cs`:

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Data aspect for the WorkItems module — what to carry in the package.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Data</c>.
/// Links and Attachments are intrinsic core behaviour — always carried, not configurable.
/// </summary>
public sealed class WorkItemsDataOptions
{
    /// <summary>Revision history export. Default: enabled.</summary>
    public EnabledExtensionOptions Revisions { get; init; } = new();

    /// <summary>Comments. Default: enabled, no deleted comments.</summary>
    public CommentsExtensionOptionsConfig Comments { get; init; } = new();

    /// <summary>Embedded images. Default: enabled, 30 s timeout.</summary>
    public EmbeddedImagesExtensionOptionsConfig EmbeddedImages { get; init; } = new();
}
```

`WorkItemsProcessingOptions.cs`:

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Processing aspect for the WorkItems module — how import/export execute.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Processing</c>.
/// </summary>
public sealed class WorkItemsProcessingOptions
{
    /// <summary>Work item resolution strategy for import. Default: not configured.</summary>
    public WorkItemResolutionStrategyOptionsConfig WorkItemResolutionStrategy { get; init; } = new();
}
```

`WorkItemsModuleOptions.cs` becomes:

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Configuration for the WorkItems module.
/// Bound from <c>MigrationPlatform:Modules:WorkItems</c>.
/// Anatomy per .agents/10-contracts/specs/module-anatomy-contract.md (ConfigVersion 2.0):
/// Selection (what to migrate), Data (what to carry), Processing (how to execute).
/// </summary>
#if NET7_0_OR_GREATER
public sealed class WorkItemsModuleOptions : IConfigSection
#else
public sealed class WorkItemsModuleOptions
#endif
{
    /// <summary>Configuration section name.</summary>
    public static string SectionName => "MigrationPlatform:Modules:WorkItems";

    /// <summary>Whether this module participates in the current run. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Selection aspect: WIQL query and field-level filters.</summary>
    public WorkItemsSelectionOptions Selection { get; init; } = new();

    /// <summary>Data aspect: revisions, comments, embedded images.</summary>
    public WorkItemsDataOptions Data { get; init; } = new();

    /// <summary>Processing aspect: resolution strategy and runtime policies.</summary>
    public WorkItemsProcessingOptions Processing { get; init; } = new();
}
```

Delete `WorkItemsScopeOptions.cs` and `WorkItemsExtensionsOptions.cs`. Also update `WorkItemsModuleExtensions.FromOptions` (`src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/WorkItemsModuleExtensions.cs`): `options.Scope.Filters` → `options.Selection.Filters` (line 64), `options.Extensions.WorkItemResolutionStrategy` → `options.Processing.WorkItemResolutionStrategy` (line 87), `options.Scope.Query` → `options.Selection.Query` (line 90). Fix compile fallout in `tests/.../WorkItemsModuleImportTests.cs` and `tests/.../WorkItemOrchestratorFilterTests.cs` by the same renames (`Scope = new WorkItemsScopeOptions {...}` → `Selection = new WorkItemsSelectionOptions {...}`, `Extensions = new WorkItemsExtensionsOptions {...}` → `Data = new WorkItemsDataOptions {...}` / `Processing = new WorkItemsProcessingOptions {...}` depending on which sub-option each test sets).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build DevOpsMigrationPlatform.sln 2>&1 | tail -5` then `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Tests --filter "FullyQualifiedName~MigrationPlatformOptionsDeserializationTests" 2>&1 | tail -5` and `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~WorkItemsModuleImportTests|FullyQualifiedName~WorkItemOrchestratorFilterTests" 2>&1 | tail -5`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(config)!: WorkItemsModuleOptions Selection/Data/Processing anatomy (MC-H2)"
```

### Task 5: Teams options anatomy

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/TeamsModuleOptions.cs`
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsOrchestrator.cs` (~lines 595–634, 818)
- Test: existing Teams module tests (locate with `grep -rl "TeamsModuleOptions" tests/`)

- [ ] **Step 1: Write the failing test** — in the existing Teams options/orchestrator test file found by the grep, change option construction to the new shape, e.g.:

```csharp
    var options = new TeamsModuleOptions
    {
        Enabled = true,
        Selection = new TeamsSelectionOptions { Scope = "teams", Filter = "^Alpha" },
        Data = new TeamsDataOptions { TeamCapacity = false },
        Processing = new TeamsProcessingOptions { AlwaysExport = true }
    };
```

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~Teams" 2>&1 | tail -20`
Expected: FAIL — `'TeamsModuleOptions' does not contain a definition for 'Selection'`.

- [ ] **Step 3: Implement** — replace the body of `TeamsModuleOptions.cs`:

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if NET7_0_OR_GREATER
using DevOpsMigrationPlatform.Abstractions.Options;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Selection aspect for the TeamsModule — which teams to migrate.</summary>
public sealed class TeamsSelectionOptions
{
    /// <summary>Scope type: <c>"all"</c> (default) or <c>"teams"</c> (apply <see cref="Filter"/>).</summary>
    public string Scope { get; init; } = "all";

    /// <summary>Optional case-insensitive regex filter applied to team names when Scope is <c>"teams"</c>.</summary>
    public string Filter { get; init; } = string.Empty;
}

/// <summary>Data aspect for the TeamsModule — which team payloads to carry.</summary>
public sealed class TeamsDataOptions
{
    /// <summary>Export/import team settings (backlog level, bugs behaviour, working days).</summary>
    public bool TeamSettings { get; init; } = true;

    /// <summary>Export/import team iteration assignments.</summary>
    public bool TeamIterations { get; init; } = true;

    /// <summary>Export/import team members with admin flags.</summary>
    public bool TeamMembers { get; init; } = true;

    /// <summary>Export/import per-member per-sprint capacity data.</summary>
    public bool TeamCapacity { get; init; } = true;
}

/// <summary>Processing aspect for the TeamsModule — runtime behaviour policies.</summary>
public sealed class TeamsProcessingOptions
{
    /// <summary>Force fresh export of every team even when its package artefact exists. Default: false (resumable).</summary>
    public bool AlwaysExport { get; init; } = false;

    /// <summary>Record team area/iteration paths into ReferencedPathTracker during export (NodeTranslation seam).</summary>
    public bool NodeTranslation { get; init; } = true;

    /// <summary>Resolve team member identities via <c>IdentityTranslationTool</c> (IdentityLookup seam).</summary>
    public bool IdentityLookup { get; init; } = true;
}

/// <summary>Options for the TeamsModule (ConfigVersion 2.0 anatomy).</summary>
#if NET7_0_OR_GREATER
public sealed class TeamsModuleOptions : IConfigSection
#else
public sealed class TeamsModuleOptions
#endif
{
    /// <summary>Configuration section name.</summary>
    public static string SectionName => "MigrationPlatform:Modules:Teams";

    /// <summary>Whether the module is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Selection aspect: team scope and name filter.</summary>
    public TeamsSelectionOptions Selection { get; init; } = new();

    /// <summary>Data aspect: settings, iterations, members, capacity.</summary>
    public TeamsDataOptions Data { get; init; } = new();

    /// <summary>Processing aspect: re-export, node translation, identity lookup.</summary>
    public TeamsProcessingOptions Processing { get; init; } = new();
}
```

Delete `TeamsModuleExtensionsOptions`. In `TeamsOrchestrator.cs` apply renames: `options.Scope` → `options.Selection.Scope`, `options.Filter` → `options.Selection.Filter`, `options.AlwaysExport` → `options.Processing.AlwaysExport`. Callers currently passing `options.Extensions` (lines ~634, 818) pass `options.Data` and `options.Processing` (or both objects) — update the receiving method signatures (`TeamsModuleExtensionsOptions` parameters) to take `TeamsDataOptions data, TeamsProcessingOptions processing` and rename member reads accordingly (`extensions.TeamSettings` → `data.TeamSettings`, `extensions.NodeTranslation` → `processing.NodeTranslation`, `extensions.IdentityLookup` → `processing.IdentityLookup`, etc.). Run `grep -rn "TeamsModuleExtensionsOptions\|Extensions.TeamSettings\|Extensions.TeamIterations\|Extensions.TeamMembers\|Extensions.TeamCapacity\|Extensions.NodeTranslation\|Extensions.IdentityLookup" src tests` and fix every hit with this table.

- [ ] **Step 4: Verify GREEN**

Run: `dotnet build DevOpsMigrationPlatform.sln 2>&1 | tail -5 && dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~Teams" 2>&1 | tail -5`
Expected: build clean, tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(config)!: TeamsModuleOptions Selection/Data/Processing anatomy (MC-H1)"
```

### Task 6: Nodes and Identities options anatomy

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/NodesModuleOptions.cs`, `IdentitiesModuleOptions.cs`
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs` (~lines 183, 191), `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/IdentityMappingService.cs` (~84–85), `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityTranslation/IdentityTranslationTool.cs` (~39, 145–148)
- Test: existing Nodes/Identities tests (locate with `grep -rl "NodesModuleOptions\|IdentitiesModuleOptions" tests/`)

- [ ] **Step 1: Write the failing test** — update the located tests to construct:

```csharp
    var nodes = new NodesModuleOptions { Processing = new NodesProcessingOptions { ReplicateSourceTree = true } };
    var identities = new IdentitiesModuleOptions { Processing = new IdentitiesProcessingOptions { DefaultIdentity = "svc@example.com" } };
```

- [ ] **Step 2: RED**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests --filter "FullyQualifiedName~Nodes|FullyQualifiedName~Identit" 2>&1 | tail -20`
Expected: FAIL — no `Processing` member.

- [ ] **Step 3: Implement**

`NodesModuleOptions.cs`:

```csharp
/// <summary>Processing aspect for the NodesModule.</summary>
public sealed class NodesProcessingOptions
{
    /// <summary>When true, the full source classification tree is replicated to the target during import.</summary>
    public bool ReplicateSourceTree { get; init; }
}
```

and on `NodesModuleOptions` replace `public bool ReplicateSourceTree { get; init; }` with:

```csharp
    /// <summary>Processing aspect: import-phase tree replication policy.</summary>
    public NodesProcessingOptions Processing { get; init; } = new();
```

`IdentitiesModuleOptions.cs`:

```csharp
/// <summary>Processing aspect for the IdentitiesModule.</summary>
public sealed class IdentitiesProcessingOptions
{
    /// <summary>Default identity when resolution fails. Falls back to the source identity string when empty.</summary>
    public string DefaultIdentity { get; init; } = string.Empty;
}
```

and replace `DefaultIdentity` on the module options with `public IdentitiesProcessingOptions Processing { get; init; } = new();`. Apply consumer renames: `_options.ReplicateSourceTree` → `_options.Processing.ReplicateSourceTree`; `_options.DefaultIdentity` → `_options.Processing.DefaultIdentity` (three files listed above; confirm no others with `grep -rn "\.ReplicateSourceTree\|\.DefaultIdentity" src tests`).

- [ ] **Step 4: GREEN**

Run: `dotnet build DevOpsMigrationPlatform.sln 2>&1 | tail -5 && dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests 2>&1 | tail -5`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(config)!: Nodes/Identities Processing anatomy (MC-H1)"
```

### Task 7: ConfigVersion 2.0 gate in `MigrationPlatformOptionsValidator`

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformOptionsValidator.cs`
- Test: `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Config/ConfigVersionGateTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Config;

[TestClass]
public class ConfigVersionGateTests
{
    private static MigrationPlatformOptions ValidExportOptions(string configVersion) => new()
    {
        ConfigVersion = configVersion,
        Mode = "Export",
        Source = new MigrationEndpointOptions { Type = "Simulated", Project = "P" },
        Package = new MigrationPackageOptions { WorkingDirectory = "C:/tmp/pkg" }
    };

    [TestMethod]
    [TestCategory("L0")]
    public void Validate_ConfigVersion1_FailsWithUpgradeInstructions()
    {
        var validator = new MigrationPlatformOptionsValidator();
        var result = validator.Validate(null, ValidExportOptions("1.0"));

        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.FailureMessage, "configuration version '1.0', which is no longer supported");
        StringAssert.Contains(result.FailureMessage, "requires ConfigVersion '2.0'");
        StringAssert.Contains(result.FailureMessage, "Rename 'Scope' to 'Selection'");
    }

    [TestMethod]
    [TestCategory("L0")]
    public void Validate_ConfigVersion2_Passes()
    {
        var validator = new MigrationPlatformOptionsValidator();
        var result = validator.Validate(null, ValidExportOptions("2.0"));
        Assert.IsTrue(result.Succeeded, result.FailureMessage);
    }
}
```

(If `MigrationPlatformOptionsValidator` is `internal`, add its name to the existing `InternalsVisibleTo` for `DevOpsMigrationPlatform.Infrastructure.Tests`, or follow how `MigrationPlatformOptionsValidator` is already exercised by existing tests in that project — reuse that mechanism.)

- [ ] **Step 2: RED**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Tests --filter "FullyQualifiedName~ConfigVersionGateTests" 2>&1 | tail -20`
Expected: FAIL — v1 currently passes validation (no ConfigVersion check exists).

- [ ] **Step 3: Implement** — in `MigrationPlatformOptionsValidator.Validate`, before the Mode check:

```csharp
        // ConfigVersion — hard cutover to 2.0 (ADR 0028). No v1 shim.
        if (!string.Equals(options.ConfigVersion, "2.0", StringComparison.Ordinal))
        {
            errors.Add(
                $"This file uses configuration version '{options.ConfigVersion}', which is no longer supported. " +
                "This release requires ConfigVersion '2.0'.\n" +
                "Module options are now expressed as three aspects: 'Selection' (what to migrate), 'Data' (what to carry), 'Processing' (how to execute).\n" +
                "To upgrade 'Modules.WorkItems':\n" +
                "  1. Rename 'Scope' to 'Selection' ('Query' and 'Filters' are unchanged).\n" +
                "  2. Move 'Extensions.Revisions', 'Extensions.Comments', and 'Extensions.EmbeddedImages' under 'Data'.\n" +
                "  3. Move 'Extensions.WorkItemResolutionStrategy' under 'Processing'.\n" +
                "  4. Delete the now-empty 'Extensions' object.\n" +
                "  5. Set 'MigrationPlatform.ConfigVersion' to '2.0'.\n" +
                "See docs/configuration-reference.md ('Module configuration anatomy') for the full v2 layout.");
        }
```

- [ ] **Step 4: GREEN**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Tests --filter "FullyQualifiedName~ConfigVersionGateTests" 2>&1 | tail -5`
Expected: PASS. Existing validator tests that used v1/empty ConfigVersion must be updated to `"2.0"` — run `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Tests 2>&1 | tail -5` and fix fallout.

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(config)!: require ConfigVersion 2.0 with actionable upgrade error (MC-H2)"
```

### Task 8: v1-shape detection in `ConfigurationService` (JSON-level, precise key errors)

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Infrastructure/Config/ConfigurationService.cs` (`LoadConfigurationAsync` after the ConfigVersion-presence check ~line 97; `SaveConfigurationAsync` line 206)
- Test: `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Config/ConfigVersionGateTests.cs` (extend) — reuse how existing `ConfigurationService` tests in that project construct the service (logger + temp file)

- [ ] **Step 1: Write the failing tests** (append; adapt construction to the existing ConfigurationService test pattern in this project):

```csharp
    [TestMethod]
    [TestCategory("L0")]
    public async Task Load_V1Config_FailsWithUpgradeInstructions()
    {
        var path = WriteTempConfig("""
        { "MigrationPlatform": { "ConfigVersion": "1.0", "Mode": "Export",
          "Modules": { "WorkItems": { "Enabled": true, "Scope": { "Query": "Q" }, "Extensions": { "Revisions": { "Enabled": true } } } } } }
        """);
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => CreateService().LoadConfigurationAsync(path));
        StringAssert.Contains(ex.Message, "no longer supported");
        StringAssert.Contains(ex.Message, "Rename 'Scope' to 'Selection'");
    }

    [TestMethod]
    [TestCategory("L0")]
    public async Task Load_V2ConfigWithStrayLegacyKeys_NamesTheKeys()
    {
        var path = WriteTempConfig("""
        { "MigrationPlatform": { "ConfigVersion": "2.0", "Mode": "Export",
          "Modules": { "WorkItems": { "Enabled": true, "Scope": { "Query": "Q" } } } } }
        """);
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => CreateService().LoadConfigurationAsync(path));
        StringAssert.Contains(ex.Message, "legacy key(s) 'Scope'");
        StringAssert.Contains(ex.Message, "removed in ConfigVersion 2.0");
    }
```

- [ ] **Step 2: RED**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Tests --filter "FullyQualifiedName~ConfigVersionGateTests" 2>&1 | tail -20`
Expected: FAIL — v1 file currently loads successfully.

- [ ] **Step 3: Implement** — in `LoadConfigurationAsync`, after the ConfigVersion-presence check (line ~97):

```csharp
            var configVersion = platformElement.GetProperty("ConfigVersion").GetString();
            if (!string.Equals(configVersion, "2.0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Configuration error in '{actualConfigPath}': this file uses configuration version '{configVersion}', which is no longer supported. This release requires ConfigVersion '2.0'.\n" +
                    "Module options are now expressed as three aspects: 'Selection' (what to migrate), 'Data' (what to carry), 'Processing' (how to execute).\n" +
                    "To upgrade 'Modules.WorkItems':\n" +
                    "  1. Rename 'Scope' to 'Selection' ('Query' and 'Filters' are unchanged).\n" +
                    "  2. Move 'Extensions.Revisions', 'Extensions.Comments', and 'Extensions.EmbeddedImages' under 'Data'.\n" +
                    "  3. Move 'Extensions.WorkItemResolutionStrategy' under 'Processing'.\n" +
                    "  4. Delete the now-empty 'Extensions' object.\n" +
                    "  5. Set 'MigrationPlatform.ConfigVersion' to '2.0'.\n" +
                    "See docs/configuration-reference.md ('Module configuration anatomy') for the full v2 layout.");
            }

            // ConfigVersion says 2.0 — reject stray v1 module keys so nothing binds silently.
            if (platformElement.TryGetProperty("Modules", out var modulesElement)
                && modulesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var module in modulesElement.EnumerateObject())
                {
                    if (module.Value.ValueKind != JsonValueKind.Object) continue;
                    var legacyKeys = new List<string>();
                    if (module.Value.TryGetProperty("Scope", out var scopeEl) && scopeEl.ValueKind == JsonValueKind.Object)
                        legacyKeys.Add("Scope");
                    if (module.Value.TryGetProperty("Extensions", out _))
                        legacyKeys.Add("Extensions");
                    if (legacyKeys.Count > 0)
                        throw new InvalidOperationException(
                            $"Configuration error in '{actualConfigPath}': 'Modules.{module.Name}' contains legacy key(s) " +
                            $"{string.Join(", ", legacyKeys.Select(k => $"'{k}'"))} which were removed in ConfigVersion 2.0. " +
                            "Rename 'Scope' to 'Selection'; move 'Extensions.Revisions'/'Comments'/'EmbeddedImages' under 'Data' " +
                            "and 'Extensions.WorkItemResolutionStrategy' under 'Processing'. See docs/configuration-reference.md.");
                }
            }
```

Note the `scopeEl.ValueKind == JsonValueKind.Object` guard: Teams' v2 `Selection.Scope` is a *string inside Selection*, not a top-level object, and no v2 module has a top-level `Scope` object — but Teams v1 had a top-level `Scope` **string**; extend the check to `scopeEl.ValueKind is JsonValueKind.Object or JsonValueKind.String` so Teams v1 files are caught too.

In `SaveConfigurationAsync` change line 206:

```csharp
            platformNode.Add("ConfigVersion", System.Text.Json.Nodes.JsonValue.Create("2.0"));
```

This automatically fixes the `config new` wizard output (`ConfigNewCommand` → `IConfigurationService.SaveConfigurationAsync`); the serialized `Modules` shape follows the new options types with no wizard code change. Verify no other wizard emitter exists: `grep -rn "Scope\|Extensions" src/DevOpsMigrationPlatform.CLI.Migration/Configuration/` (expected: no config-shape hits).

- [ ] **Step 4: GREEN**

Run: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Tests 2>&1 | tail -5`
Expected: PASS (update any existing ConfigurationService tests still writing v1 fixtures to v2).

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "feat(config)!: reject v1 config shapes at load with precise rewrite guidance (MC-H2)"
```

### Task 9: Regenerate `migration.schema.json` + schema tests

**Files:**
- Modify: `src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json` (generated)
- Modify: `tests/DevOpsMigrationPlatform.SchemaGenerator.Tests/SchemaGeneratorHostTests.cs`, `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Config/JsonSchemaConfigValidatorTests.cs` (fixtures asserting old shape)

- [ ] **Step 1: Write the failing test** — in `SchemaGeneratorHostTests`, add/adjust an assertion that the generated schema's `WorkItemsModuleOptions` definition contains `Selection`, `Data`, `Processing` and NOT `Scope`/`Extensions`:

```csharp
    StringAssert.Contains(schemaJson, "\"Selection\"");
    StringAssert.Contains(schemaJson, "\"Data\"");
    StringAssert.Contains(schemaJson, "\"Processing\"");
    Assert.IsFalse(schemaJson.Contains("WorkItemsScopeOptions"));
    Assert.IsFalse(schemaJson.Contains("WorkItemsExtensionsOptions"));
```

- [ ] **Step 2: RED** — only if Tasks 4–6 aren't merged into the test's generation path yet; otherwise this may already pass because the generator reflects the live types. Run: `dotnet test tests/DevOpsMigrationPlatform.SchemaGenerator.Tests 2>&1 | tail -10`. If GREEN immediately, note that in the session log and continue (the RED phase for this task is the pre-Task-4 state).

- [ ] **Step 3: Regenerate the committed schema**

Run: `dotnet run --project src/DevOpsMigrationPlatform.SchemaGenerator -- --output src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json`
Expected: exit 0; `git diff src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json` shows `Scope`/`Extensions` replaced by `Selection`/`Data`/`Processing` under all four module definitions.

- [ ] **Step 4: GREEN**

Run: `dotnet test tests/DevOpsMigrationPlatform.SchemaGenerator.Tests tests/DevOpsMigrationPlatform.Infrastructure.Tests 2>&1 | tail -5` — fix `JsonSchemaConfigValidatorTests` fixtures (v1 JSON → v2).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json tests
git commit -m "feat(schema)!: regenerate migration.schema.json for ConfigVersion 2.0 anatomy (MC-H2)"
```

### Task 10: Migrate all 16 scenario/sample configs + appsettings

**Files:** the 16 JSON files enumerated in the file map, plus `src/DevOpsMigrationPlatform.CLI.Migration/appsettings.json`.

- [ ] **Step 1: Mechanical migration** — for every file: set `"ConfigVersion": "2.0"`; inside `Modules.WorkItems` rename `"Scope"` → `"Selection"`; split `"Extensions"`: `Revisions`/`Comments`/`EmbeddedImages` → `"Data"`, `WorkItemResolutionStrategy` → `"Processing"`; delete legacy `"Links"`/`"Attachments"` extension entries (intrinsic since the extensions purge — e.g. `SystemTest-Simulated-Export-WorkItems.json` still carries `"Attachments": { "Enabled": true }`; drop it). For `Modules.Teams` (in `SystemTest-Live-Migrate-AzureDevOps-Complete.json` and any other file with Teams config): move `Scope`/`Filter` under `"Selection"`, `AlwaysExport` under `"Processing"`, and split `Extensions` per the aspect-mapping table. `appsettings.json` needs only the ConfigVersion bump (it has no Modules section).

- [ ] **Step 2: Verify no stragglers**

Run: `grep -rln '"ConfigVersion": "1.0"' scenarios src docs && grep -rln '"Extensions"' scenarios src/DevOpsMigrationPlatform.CLI.Migration/appsettings.json`
Expected: no output (the schema file was regenerated in Task 9; `docs` handled in Task 13).

- [ ] **Step 3: Validate schemas** — run the simulated system-test config end-to-end (RED→GREEN evidence comes in Task 12's system test):

Run: `dotnet build DevOpsMigrationPlatform.sln 2>&1 | tail -3`
Expected: clean build.

- [ ] **Step 4: Commit**

```bash
git add scenarios src/DevOpsMigrationPlatform.CLI.Migration/appsettings.json
git commit -m "chore(config)!: migrate all scenario and sample configs to ConfigVersion 2.0"
```

### Task 11: Migrate C# test fixtures and builders

**Files:**
- Modify: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Cli/TfsExport/TfsExportBuilder.cs` (`BuildConfigJson()`, lines ~280–338)
- Modify: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/QueueCommandTests.cs` (inline config JSON)
- Modify: any remaining hits from the sweep

- [ ] **Step 1: Sweep for embedded v1 JSON**

Run: `grep -rln '"ConfigVersion": "1.0"\|"ConfigVersion":"1.0"' tests src`
Expected output: the two files above (plus any others — fix all of them).

- [ ] **Step 2: Migrate** — in `TfsExportBuilder.BuildConfigJson()`, both raw-string templates: `"ConfigVersion": "1.0"` → `"2.0"`; in the simulated branch replace:

```json
                "Modules": {
                  "WorkItems": {
                    "Enabled": true,
                    "Data": {
                      "Revisions": { "Enabled": true },
                      "Comments": { "Enabled": true }
                    }
                  }
                }
```

(the v1 `"Attachments"` extension entry is dropped — intrinsic). Apply the same mechanical migration to `QueueCommandTests.cs` inline JSON and every other sweep hit.

- [ ] **Step 3: GREEN**

Run: `dotnet test tests/DevOpsMigrationPlatform.CLI.Migration.Tests 2>&1 | tail -5`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests
git commit -m "test(config): migrate CLI test fixture builders to ConfigVersion 2.0"
```

### Task 12: System tests — v1 fails actionably, v2 runs end-to-end (Simulated)

**Files:**
- Test: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Cli/TfsExport/TfsExportConfigVersionTests.cs` (create, reusing `TfsExportBuilder` patterns)
- Modify: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Cli/TfsExport/TfsExportBuilder.cs` — add a `WithLegacyV1Config()` variant

- [ ] **Step 1: Write the failing tests**

Add to `TfsExportBuilder`:

```csharp
    private bool _useLegacyV1Config;

    /// <summary>Writes a deliberately-legacy v1 config (ConfigVersion 1.0, Scope/Extensions) to prove the hard-cutover rejection path.</summary>
    public TfsExportBuilder WithLegacyV1Config()
    {
        _useLegacyV1Config = true;
        _useSimulatedSource = true;
        return this;
    }
```

and in `BuildConfigJson()`, before the simulated branch:

```csharp
        if (_useLegacyV1Config)
        {
            return $$"""
            {
              "MigrationPlatform": {
                "ConfigVersion": "1.0",
                "Mode": "Export",
                "Source": { "Type": "Simulated", "Project": "SimulatedProject" },
                "Package": { "WorkingDirectory": "{{packageDir}}", "CreatePackage": true },
                "Modules": {
                  "WorkItems": {
                    "Enabled": true,
                    "Scope": { "Query": "SELECT [System.Id] FROM WorkItems" },
                    "Extensions": { "Revisions": { "Enabled": true } }
                  }
                }
              }
            }
            """;
        }
```

New test file:

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

[TestClass]
public class TfsExportConfigVersionTests
{
    [TestMethod]
    [TestCategory("L2")]
    public async Task Queue_WithV1Config_FailsWithActionableUpgradeMessage()
    {
        await using var result = await new TfsExportBuilder()
            .WithLegacyV1Config()
            .RunOutOfProcessAsync();

        Assert.AreNotEqual(0, result.ExitCode);
        StringAssert.Contains(result.StandardError + result.StandardOutput, "no longer supported");
        StringAssert.Contains(result.StandardError + result.StandardOutput, "Rename 'Scope' to 'Selection'");
    }

    [TestMethod]
    [TestCategory("L2")]
    public async Task Queue_WithV2SimulatedConfig_ExportsEndToEnd()
    {
        await using var result = await new TfsExportBuilder()
            .WithSimulatedSource()
            .RunOutOfProcessAsync();

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
    }
}
```

- [ ] **Step 2: RED**

Run: `dotnet test tests/DevOpsMigrationPlatform.CLI.Migration.Tests --filter "FullyQualifiedName~TfsExportConfigVersionTests" 2>&1 | tail -20`
Expected: v1 test FAILS only if Tasks 7–8 are incomplete; with them done, both should PASS — if the v1 test passes immediately, capture the transcript as the required contract-compatibility evidence. If the actionable message doesn't surface on stderr, trace how `QueueCommand` reports `ConfigurationService` load failures and assert on the actual channel.

- [ ] **Step 3: GREEN**

Run: same command. Expected: 2 PASS.

- [ ] **Step 4: Commit**

```bash
git add tests
git commit -m "test(system): v1 config rejected with rewrite guidance; v2 Simulated export end-to-end (MC-H2)"
```

### Task 13: Documentation migration

**Files:**
- Modify: `docs/configuration-reference.md`, `docs/operator-guide.md`, `docs/capabilities-guide.md`, `docs/modules.md`
- Modify: `src/DevOpsMigrationPlatform.Abstractions/Options/MigrationPlatformOptions.cs` (XML-doc example, lines 13–39)

- [ ] **Step 1: Update `MigrationPlatformOptions` XML-doc example** — replace the `<code>` block with the v2 JSON from the "Config migration story" section (ConfigVersion "2.0", Selection/Data/Processing) and update the `ConfigVersion` property doc to `/// <summary>Schema version of this configuration file. Must be "2.0".</summary>`.

- [ ] **Step 2: Update docs** — in each of the three guides, migrate every `Modules.*.Scope`/`Extensions` JSON example to the v2 shape (same mechanical mapping as Task 10). In `docs/configuration-reference.md` add a section titled **"Module configuration anatomy"** (the anchor the error message cites) containing: the three-aspect explanation, the v1→v2 side-by-side JSON from this plan, and the upgrade steps 1–5 verbatim. In `docs/modules.md` document `IModule.Contract` / `IModuleContract` and the aspect responsibilities.

- [ ] **Step 3: Verify**

Run: `grep -rn '"Scope"\|"Extensions"' docs/*.md`
Expected: hits only inside the deliberate v1 "before" example in the migration section of `configuration-reference.md`.

- [ ] **Step 4: Commit**

```bash
git add docs src/DevOpsMigrationPlatform.Abstractions/Options/MigrationPlatformOptions.cs
git commit -m "docs(config): document ConfigVersion 2.0 module anatomy and v1 upgrade path"
```

### Task 14: ADR 0028 — Class C consent record

**Files:**
- Create: `docs/adr/0028-module-anatomy-selection-data-processing-config.md` (0028 is the next free number; re-check with `ls docs/adr` before writing)

- [ ] **Step 1: Write the ADR** following the structure of `docs/adr/0027-real-teams-nodes-prepare-and-module-dependency-contract.md`:

```markdown
# 0028 — Module Anatomy: Selection/Data/Processing Configuration Contract (ConfigVersion 2.0)

## Status

Accepted (2026-07-03)

## Context

The module-anatomy contract (.agents/10-contracts/specs/module-anatomy-contract.md) mandates that
module configuration uses exactly three aspects — Selection, Data, Processing — surfaced through
IModule.Contract / IModuleContract, and declares Scope/Extensions legacy. Architecture audit items
MC-H1 and MC-H2 (analysis/archcheck/triage.json) found the contract surface unimplemented and
WorkItemsModuleOptions still shaped as Scope/Extensions. Both items are change-class C: they widen
IModule and replace the public configuration contract (migration.schema.json).

## Decision

Implement MC-H1 and MC-H2 together as one clean break:

- New canonical interfaces IModuleContract, ISelectionDefinition, IDataDefinition,
  IProcessingDefinition; IModule gains a platform-owned, non-user-editable Contract property.
- All four modules (WorkItems, Teams, Nodes, Identities) restructure their options into
  Selection / Data / Processing. Every v1 property maps 1:1 to a v2 home (see the aspect-mapping
  table in docs/superpowers/plans/2026-07-03-module-config-contract.md).
- MigrationPlatform.ConfigVersion bumps "1.0" -> "2.0". There is NO legacy shim and NO dual-read
  path: v1 files are rejected at load and at ValidateOnStart with a step-by-step rewrite message,
  and stray legacy keys under ConfigVersion 2.0 are rejected by name.
- migration.schema.json is regenerated; the config wizard emits 2.0.

## Consent

Explicit operator ruling (2026-07-01 session): one clean break, no legacy config support,
no deprecation shim; hard cutover with a ConfigVersion bump and precise validation errors.

## Consequences

- Every existing user migration.json breaks loudly (never silently) until upgraded; the error text
  contains the full rewrite recipe and points to docs/configuration-reference.md.
- All scenario configs, test fixtures, and docs migrated in the same change.
- Future modules must declare their anatomy via IModuleContract; Scope/Extensions must not reappear.
- Capability seams referenced by Processing entries (FieldTransform, NodeTranslation,
  IdentityLookup) remain singular and canonical (contract rule 7).
```

- [ ] **Step 2: Update `docs/adr/README.md`** index if it lists ADRs.

- [ ] **Step 3: Commit**

```bash
git add docs/adr
git commit -m "docs(adr): 0028 module anatomy Selection/Data/Processing clean break (MC-H1+MC-H2)"
```

### Task 15: Full verification

- [ ] **Step 1: Full build + full test suite**

Run: `dotnet build DevOpsMigrationPlatform.sln 2>&1 | tail -3 && dotnet test DevOpsMigrationPlatform.sln 2>&1 | tail -10`
Expected: build clean; all tests PASS (Live-category tests may be excluded per the repo's normal CI filter — run the same filter CI uses).

- [ ] **Step 2: Schema snapshot check**

Run: `dotnet run --project src/DevOpsMigrationPlatform.SchemaGenerator -- --output src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json && git diff --exit-code src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json`
Expected: exit 0 (committed schema matches regenerated schema).

- [ ] **Step 3: Legacy-shape sweep (fail-closed)**

Run: `grep -rn "WorkItemsScopeOptions\|WorkItemsExtensionsOptions\|TeamsModuleExtensionsOptions" src tests; grep -rln '"ConfigVersion": "1.0"' src tests scenarios`
Expected: no hits anywhere except the deliberate v1 fixture in `TfsExportBuilder.WithLegacyV1Config()` and the v1 "before" JSON in the ConfigVersionGate tests/docs migration example.

- [ ] **Step 4: System-test evidence** — re-run Task 12's two tests and keep the transcript in the session log as the Class C "contract compatibility tests" evidence.

Run: `dotnet test tests/DevOpsMigrationPlatform.CLI.Migration.Tests --filter "FullyQualifiedName~TfsExportConfigVersionTests" 2>&1 | tail -10`
Expected: 2 PASS.

- [ ] **Step 5: Final commit (if any stragglers)**

```bash
git add -A
git commit -m "chore: verification fixes for ConfigVersion 2.0 clean break"
```

---

## Self-review checklist (run after writing/executing the plan)

1. **Spec coverage:**
   - Contract surface `IModule.Contract`, `IModuleContract`, `ISelectionDefinition`, `IDataDefinition`, `IProcessingDefinition` → Tasks 1–3.
   - Three-aspect config for all four modules → Tasks 4–6.
   - `Scope`/`Extensions` eliminated → Tasks 4–6, 9–11, 13, verified by Task 15 sweep.
   - Required-entries-cannot-be-disabled semantics → contract `Required` flags (Tasks 2–3); Links/Attachments intrinsic (Task 4 note).
   - ConfigVersion 2.0 hard break + actionable errors → Tasks 7–8, proven by Task 12.
   - Schema regeneration + tests → Task 9; wizard → Task 8 Step 3; fixtures/docs → Tasks 10–13; ADR → Task 14; Class C evidence → Tasks 12/15.
2. **Placeholder scan:** no "TBD"/"implement later"; the only deliberately deferred lookups are grep-driven fallout sweeps whose rename tables are fully specified in Tasks 5, 6, 11.
3. **Type consistency:** `WorkItemsSelectionOptions`/`WorkItemsDataOptions`/`WorkItemsProcessingOptions` names match between Task 4 definitions and Tasks 8/11/12 fixtures; `SelectionDefinition("Query", Required: true)` record signature matches Task 1; Teams aspect type names (`TeamsSelectionOptions` etc.) match between Tasks 3 and 5; error strings in Tasks 7, 8, and 12 assertions match verbatim.
