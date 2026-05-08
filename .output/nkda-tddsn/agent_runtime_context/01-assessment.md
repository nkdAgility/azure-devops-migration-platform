# TDD Safety Net Assessment: agent_runtime_context

## 1. Behaviour Model

`agent_runtime_context` materializes a queued job's configuration payload into `migration-config.json`, reads that package configuration through `PackageConfigStore`, and publishes per-job runtime views through package config, job context, and source/target endpoint accessors. The subsystem exists so singleton infrastructure services can resolve the active job's mode, package path, config version, source endpoint, and target endpoint without carrying job-specific state across completed jobs.

Primary behaviours:

- `PackageConfigStore.WriteAsync` copies a scenario config into `migration-config.json`, rejects overwrite unless forced, records metrics, logs safe operational messages, and propagates I/O failures.
- `PackageConfigStore.ReadAsync` retries a missing `migration-config.json`, fails fast with `PackageConfigNotFoundException` after exhaustion, parses configuration when present, and records/logs success and failure outcomes.
- `AgentJobContext` accepts known migration modes, rejects unknown modes, accepts absolute package paths used by supported hosts, rejects relative/empty paths, and logs mode/config-version without leaking the package path.
- `CurrentPackageConfigAccessor`, `CurrentAgentJobContextAccessor`, and `CurrentJobEndpointAccessor` publish non-null active runtime values and clear them at job completion; source and target endpoints can be cleared independently.
- Active job wrappers expose empty values when no current context exists and reflect the active accessor value when one is set.

State transitions:

- no current package/job/endpoint context → `Set` publishes active context → `Clear` returns to no current context.
- no endpoint context → set source/target independently → clear source or target independently → `Clear` removes both.
- missing config file → retry loop → success if file appears, otherwise `PackageConfigNotFoundException`.

External contracts:

- `IPackageConfigStore` reads/writes `PackagePaths.MigrationConfigFileName` via `IArtefactStore`.
- `IAgentJobContext`, `ISourceEndpointInfo`, and `ITargetEndpointInfo` are consumed by downstream infrastructure as dynamic current-job views.
- Logging must avoid customer data leakage for package paths.

Failure/rejection behaviours:

- null store/logger/factory dependencies fail with `ArgumentNullException`.
- missing source config file fails with `FileNotFoundException`.
- existing package config without force fails with `InvalidOperationException`.
- missing package config after retries fails with `PackageConfigNotFoundException`.
- invalid mode and non-absolute package path fail with `InvalidOperationException`.
- accessor `Set` methods reject null values.

Boundary conditions:

- Windows drive-rooted, UNC, and Unix rooted package paths are all valid absolute paths even when tests execute on a different host OS.
- whitespace/empty package paths are invalid.
- source and target endpoint clear operations must not clear the opposite endpoint.

## 2. Current Test Inventory

| Test file | Current protection | Assessment |
| --------- | ------------------ | ---------- |
| `PackageConfigStoreTests.cs` | Write/read happy paths, overwrite rejection, missing/corrupt config, metrics, logging, cancellation/retry behaviours. | Strong behavioural and contract coverage for package config persistence; some broad logging assertions are acceptable because observability is part of the contract. |
| `AgentJobContextTests.cs` | Invalid mode, selected valid modes, absolute/relative package path handling, safe debug logging. | Useful unit coverage, but valid-mode coverage was incomplete and existing Windows-path expectations exposed a host-OS drift risk. |
| `AgentJobContextIntegrationTests.cs` | Dynamic wrappers over active context and endpoints; logging structure. | Valuable but partly overlaps unit tests; still useful as integration-style wrapper coverage. |
| `JobAgentWorkerDispatchTests.cs` | Worker dispatch publishes/clears current package/job/endpoint context during job execution. | Important orchestration coverage, but currently excluded from the test project compile, so it cannot be counted as active safety-net evidence. |
| `TfsJobAgentWorkerTests.cs` | TFS worker config/context handling. | Relevant connector-specific coverage, not the primary fast feedback loop for this subsystem. |

## 3. Scored Tests

| Area | Score | Notes |
| ---- | ----- | ----- |
| Package config store | 8/10 | Behaviour-focused with meaningful assertions; retry tests can be slow because production backoff is real. |
| Agent job context validation | 7/10 before rebuild, 8/10 after rebuild | Existing tests named behaviours clearly; rebuilt suite covers all known modes and cross-host absolute path rules. |
| Current accessors | 4/10 before rebuild, 8/10 after rebuild | Accessor behaviour was mostly indirect; direct tests now protect set, clear, independent endpoint clearing, and null rejection. |
| Worker materialization | 5/10 | Behaviour exists in dispatch tests, but active project exclusion reduces confidence. |

## 4. Drift Risks

| Risk | Severity | Evidence | Mitigation |
| ---- | -------- | -------- | ---------- |
| Windows package paths drift on non-Windows agents because validation follows host `Path.IsPathRooted`. | High | Existing `AgentJobContextTests` use Windows rooted paths while CI/agent environments can be Linux. | Use host-independent absolute package path validation and keep tests covering Windows, UNC, and Unix roots. |
| Accessor clear semantics can regress silently. | Medium | Source and target clear operations are independent in production but were not directly tested. | Add direct accessor tests. |
| Valid mode list can drift when new runtime modes are added. | Medium | Only `Inventory` and `Dependencies` were explicitly tested as valid modes. | Parameterize valid-mode tests over remaining modes. |
| Worker context cleanup may not be actively compiled. | Medium | `JobAgentWorkerDispatchTests.cs` is removed from test compilation. | Follow up separately to re-enable or replace dispatch coverage. |

## 5. Suite-Level Gap Map

- Missing direct unit tests for `CurrentPackageConfigAccessor`, `CurrentAgentJobContextAccessor`, and `CurrentJobEndpointAccessor` set/clear/null semantics.
- Missing valid-mode coverage for `Export`, `Import`, `Prepare`, and `Migrate`.
- Missing explicit host-independent absolute path contract in subsystem documentation.
- Existing dispatch tests are not active in the project and should not be relied on as the only cleanup protection.

## 6. Recommendations

- Keep `PackageConfigStoreTests.cs` as the package config persistence safety net.
- Rewrite/extend `AgentJobContextTests.cs` to cover all valid modes through a data test.
- Add `CurrentRuntimeContextAccessorsTests.cs` for direct accessor semantics.
- Make minimal production change to `AgentJobContext` package-path validation so the contract is host-independent.
- Document the path and accessor contracts in the subsystem architecture note.
