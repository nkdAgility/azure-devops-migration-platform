# Coding Standards – Azure DevOps Migration Platform

This file defines enforceable coding standards for the migration platform.

All contributors and AI agents must follow these rules.

---

# 🎯 Purpose

Ensure:

- Determinism
- Testability
- Maintainability
- Isolation of legacy concerns
- No architectural drift

---

# 🔒 Language & Runtime

## Primary Runtime

- MUST use C# 10+.
- MUST target .NET 9 or .NET 10.
- All new code MUST be .NET 9/10 unless explicitly exempted.

## Legacy Runtime (Explicit Carve-Out)

The ONLY allowed .NET Framework usage is:

- The external TeamFoundationServer exporter
- Built against .NET 4.x
- Using the legacy TFS Object Model (SOAP)

Rules for the carve-out:

- The .NET 4 exporter MUST exist in a separate project.
- It MUST NOT share runtime dependencies with .NET 9/10 components.
- It MUST be invoked only via external process execution.
- It MUST communicate only via file output.
- It MUST NOT be referenced directly as a project dependency in the .NET 9/10 solution.
- It MUST NOT expose shared libraries consumed by modern modules.

The legacy exporter is a bounded extraction adapter only.

No other component may use .NET Framework.

---

# 🧱 Architectural Rules

- MUST follow SOLID principles.
- MUST use dependency injection.
- MUST NOT use service locator patterns.
- MUST NOT use static mutable state.
- MUST NOT perform direct file IO inside modules.
- MUST use IArtefactStore for file writes.
- MUST use IStateStore for resume/checkpoint state.
- MUST isolate modules by interface boundaries.

---

# 🧪 Testing

- MUST use MSTest as the test runner.
- MUST use Reqnroll (`Reqnroll.MSTest`) as the BDD step-binding layer for acceptance tests. Reqnroll reads Gherkin `.feature` files directly.
- Each module MUST have unit tests.
- Business logic MUST be testable in isolation.
- No integration tests may depend on live Azure DevOps unless explicitly marked with `[TestCategory("Integration")]`.
- Replay logic MUST have deterministic tests.

Preferred:
- TDD for new modules.
- Explicit validation tests for package integrity.
- Acceptance tests written as Gherkin in `tests/acceptance/` before implementation begins.

---

# 📦 Determinism Rules

- File names MUST be deterministic.
- Ordering MUST be explicit and stable.
- No non-deterministic GUID usage unless explicitly required.
- Any hash used MUST be reproducible.

---

# 🧾 Error Handling

- MUST NOT swallow exceptions silently.
- MUST use structured logging.
- MUST record module failures explicitly.
- Fail-fast unless configuration explicitly allows continue-on-error.

---

# 📊 Observability

- MUST use OpenTelemetry.
- Each module execution MUST create an activity span.
- Duration and failure metrics MUST be recorded.
- No ad-hoc console logging for runtime diagnostics.

---

# 🚫 Prohibited Patterns

- Direct Source → Target migration logic.
- Global attachment stores.
- Loading entire revision sets into memory.
- Hidden resume state outside Checkpoints/.
- Cross-module direct calls.
- .NET Framework usage outside the explicit TFS exporter boundary.
- Calling source or target APIs from within the control plane.
- Calling source or target APIs from within a Migration Agent outside of the orchestrator execution path.
- Referencing `FileSystemArtefactStore` or `AzureBlobArtefactStore` directly in module code (use `IArtefactStore`).
- Sorting `EnumerateAsync` results in memory.
- Unwrapping Key Vault secrets in the control plane.
- Writing to `Console` or `System.Console` from the Job Engine or any module.
- Emitting progress as console text instead of `IProgressSink` events.
- Placing migration execution logic in the TUI (parsing and transport selection only).

---

# 🔍 Validation Checklist

Before merging changes, verify:

- Does this code introduce new state outside IStateStore?
- Does this code introduce non-deterministic behaviour?
- Does this code violate module isolation?
- Does this code bypass IArtefactStore?
- Does this code introduce .NET Framework dependencies outside the legacy exporter?
- Does this code add migration execution logic to the control plane?
- Does this code reference a concrete artefact store implementation inside a module?
- Does this code sort EnumerateAsync results in memory?
- Does this code write to Console from the Job Engine or a module?
- Does this code place migration logic in the TUI layer?

If yes, reject.

---

# Final Rule

Modern platform code runs on .NET 9/10.

Legacy TFS Object Model is allowed only as an isolated, external extraction adapter.

No exceptions.