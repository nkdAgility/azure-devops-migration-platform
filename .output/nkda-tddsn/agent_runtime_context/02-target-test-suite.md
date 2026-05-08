# Target Test Suite: agent_runtime_context

## 1. Design Intent

The target suite should keep the runtime context safety net fast and behavioural: direct unit tests protect immutable job context validation and accessor state transitions, while package config store tests protect the artefact-store contract. Tests should prevent cross-host path validation drift and singleton current-context leakage without over-specifying implementation details.

## 2. Proposed Test Classes

- class name: `AgentJobContextTests`
  - purpose: validate job context construction, mode contract, absolute path contract, and safe logging.
  - test type emphasis: unit/design tests.
  - related production area: `AgentJobContext`.
- class name: `CurrentRuntimeContextAccessorsTests`
  - purpose: validate current package config, job context, and endpoint accessor state transitions.
  - test type emphasis: unit/design tests.
  - related production area: `CurrentPackageConfigAccessor`, `CurrentAgentJobContextAccessor`, `CurrentJobEndpointAccessor`.
- class name: `PackageConfigStoreTests`
  - purpose: validate package config persistence, read/retry/failure, metrics, and logging contracts.
  - test type emphasis: unit/contract tests with artefact-store fakes.
  - related production area: `PackageConfigStore`.
- class name: `AgentJobContextIntegrationTests`
  - purpose: validate active wrapper views over current context/accessors.
  - test type emphasis: lightweight integration/design tests.
  - related production area: `ActiveJobAgentJobContext`, `ActiveJobSourceEndpointInfo`, `ActiveJobTargetEndpointInfo`.

## 3. Proposed Tests

- test method name: `Constructor_ValidMode_Succeeds`
  - type: Unit/design test
  - status: rewrite
  - protects: accepted migration mode list.
  - drift risk: valid modes become incomplete or accidentally rejected.
  - scenario:
    - Given a mode from `Dependencies`, `Export`, `Import`, `Prepare`, or `Migrate`
    - When an `AgentJobContext` is created
    - Then the context exposes that mode
  - assertions: `Mode` equals the supplied valid mode.
  - notes: keeps the existing `Inventory` test and replaces the single `Dependencies` test with a data test.

- test method name: `Constructor_InventoryMode_Succeeds`
  - type: Unit/design test
  - status: keep
  - protects: existing inventory-mode acceptance.
  - drift risk: inventory mode rejected by future refactors.
  - scenario:
    - Given `Inventory`
    - When an `AgentJobContext` is created
    - Then construction succeeds
  - assertions: `Mode` equals `Inventory`.
  - notes: retained as existing baseline.

- test method name: `Constructor_RelativePackagePath_ThrowsInvalidOperationException`
  - type: Unit/design test
  - status: keep
  - protects: rejection of relative package paths.
  - drift risk: relative package paths leak into runtime package materialization.
  - scenario:
    - Given a relative package path
    - When an `AgentJobContext` is created
    - Then creation fails
  - assertions: exception mentions absolute path and supplied value.
  - notes: retained.

- test method name: `Constructor_UnixAbsolutePath_Succeeds`
  - type: Unit/design test
  - status: keep
  - protects: Unix absolute package paths.
  - drift risk: host-specific validation rejects supported package paths.
  - scenario:
    - Given `/tmp/package`
    - When an `AgentJobContext` is created
    - Then the path is accepted
  - assertions: `PackagePath` equals `/tmp/package`.
  - notes: retained.

- test method name: `Constructor_UNCPath_Succeeds`
  - type: Unit/design test
  - status: keep
  - protects: UNC package path acceptance.
  - drift risk: network share package paths rejected.
  - scenario:
    - Given a UNC package path
    - When an `AgentJobContext` is created
    - Then the path is accepted
  - assertions: `PackagePath` equals the UNC value.
  - notes: retained.

- test method name: `CurrentPackageConfigAccessor_SetThenClear_ExposesOnlyActiveConfiguration`
  - type: Unit/design test
  - status: add
  - protects: package config set/clear state transition.
  - drift risk: package config leaks after job completion.
  - scenario:
    - Given a package configuration
    - When it is set then cleared
    - Then the accessor exposes it only while active
  - assertions: `Current` is same object after set and null after clear.
  - notes: new direct accessor test.

- test method name: `CurrentPackageConfigAccessor_SetNull_ThrowsArgumentNullException`
  - type: Unit/design test
  - status: add
  - protects: null rejection.
  - drift risk: current-context accessors accept unusable null active values.
  - scenario:
    - Given a package config accessor
    - When null is set
    - Then `ArgumentNullException` is thrown
  - assertions: exact exception type.
  - notes: new direct accessor test.

- test method name: `CurrentAgentJobContextAccessor_SetThenClear_ExposesOnlyActiveContext`
  - type: Unit/design test
  - status: add
  - protects: job context set/clear state transition.
  - drift risk: job context leaks between jobs.
  - scenario:
    - Given a valid job context
    - When it is set then cleared
    - Then the accessor exposes it only while active
  - assertions: `Current` is same object after set and null after clear.
  - notes: new direct accessor test.

- test method name: `CurrentAgentJobContextAccessor_SetNull_ThrowsArgumentNullException`
  - type: Unit/design test
  - status: add
  - protects: null rejection.
  - drift risk: dynamic wrappers see invalid current context state.
  - scenario:
    - Given a job context accessor
    - When null is set
    - Then `ArgumentNullException` is thrown
  - assertions: exact exception type.
  - notes: new direct accessor test.

- test method name: `CurrentJobEndpointAccessor_ClearSource_DoesNotClearTarget`
  - type: Unit/design test
  - status: add
  - protects: independent source clearing.
  - drift risk: source cleanup accidentally drops active target endpoint.
  - scenario:
    - Given source and target endpoints are active
    - When source is cleared
    - Then source is null and target remains active
  - assertions: source null, target same instance.
  - notes: new direct accessor test.

- test method name: `CurrentJobEndpointAccessor_ClearTarget_DoesNotClearSource`
  - type: Unit/design test
  - status: add
  - protects: independent target clearing.
  - drift risk: target cleanup accidentally drops active source endpoint.
  - scenario:
    - Given source and target endpoints are active
    - When target is cleared
    - Then target is null and source remains active
  - assertions: source same instance, target null.
  - notes: new direct accessor test.

- test method name: `CurrentJobEndpointAccessor_Clear_RemovesSourceAndTarget`
  - type: Unit/design test
  - status: add
  - protects: full endpoint cleanup.
  - drift risk: endpoints leak after job completion.
  - scenario:
    - Given source and target endpoints are active
    - When the accessor is cleared
    - Then both endpoints are removed
  - assertions: source null and target null.
  - notes: new direct accessor test.

- test method name: `CurrentJobEndpointAccessor_SetNullEndpoint_ThrowsArgumentNullException`
  - type: Unit/design test
  - status: add
  - protects: source/target null rejection.
  - drift risk: endpoint accessors publish invalid current values.
  - scenario:
    - Given an endpoint accessor
    - When null source or target is set
    - Then `ArgumentNullException` is thrown
  - assertions: exact exception type for source and target.
  - notes: new direct accessor test.

## 4. Required Test Support

- simple in-memory `IConfiguration` built with `ConfigurationBuilder` for package config accessor tests.
- record-based `ISourceEndpointInfo` and `ITargetEndpointInfo` fakes for endpoint accessor tests.
- no deterministic clock, scheduler, ID provider, or storage fake required for the new direct unit tests.

## 5. Explicit Non-Goals

- Do not rebuild `PackageConfigStoreTests.cs`; it already covers the package artefact contract adequately.
- Do not re-enable excluded `JobAgentWorkerDispatchTests.cs` in this pass because that is broader worker orchestration scope.
- Do not introduce live connector or filesystem tests; accessor and path validation behaviours are unit-testable.
