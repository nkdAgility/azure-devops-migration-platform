# Sharing this repository's work through two plugins: assessment

Date: 2026-11-13. Second pass, replacing the first. Nothing was changed apart from this file, which is not committed.

The goal: share the work in this repository with other users, through two plugins published from the `NKDAEngineering-Delivery` marketplace.

- **nkda-engineering-delivery**: how work moves (gates, workers, worktrees, review, grooming, delivery).
- **nkda-engineering-practice**: how to engineer (architecture checks, the TDD safety net, test quality, debugging, refactoring, red-team review).

Verdicts: **SHARE-DELIVERY**, **SHARE-PRACTICE** (move as it is), **SHARE AFTER SPLIT** (move the method; the named repository-specific text becomes data this repository provides), **REPO-ONLY**, **REMOVE**, **UPSTREAM** (vendored; never copied, declared as a dependency).

## How much was read

Read at `origin/main` (`3832f1ef`; local `main` is now at the same commit).

- **Read in full, every line:** the six `nkda-archcheck-*` skills; `nkda-archimprove-codebase` (4 files), `-red-team-review`, `-test-promotion`, `-test-validity`; the seven `nkda-tddsn-*` skills; `nkd-tdd-assessment`; `.agents/skill-sets/nkda-tddsn` (5 files); the five tddsn agents; the six own prompts; the four own commands; both `source-command-*` skills; `nkda-core-implementation-` and `-tasks-architecture-compliance`; `nkda-debugtests`; `nkda-refactor`; `start-session`, `end-session`, `fix`, `review`, `session-hooks` (and its README); both backlog skills; both backlog workflows; `enforce-checklists.ps1`, `enforce-task-coverage.ps1`, `verify-maintainer-label.sh`; `configure.ps1`; both git hooks; both issue templates; the PR template; `consent-policy.yaml`; ADR-0034; `backlog-item-workflow.md`, `completion-protocol.md`, `backlog-item-dod.md`, `live-test-environment.md`; the whole plugin source.
- **Read in full in a third pass, by four reader agents I briefed, not by me line by line (91 files):** `connector-coverage-check`, `nkda-core-definition-of-done`, `nkda-core-plan-architecture-compliance`, `nkda-observability-contract`, `update-docs`, `nkda-archimprove-documentation` (7 files); `nkda-archcheck-workflow.js`, `nkda-testcategory-workflow.js`; `validate-orchestrator.ps1`, `validate-evidence.py`, `validate-agent-contracts.py`; `governance.yml`; the evidence schema and its README; the six contract schemas; `reading-order.md`; `.github/github-app.yml`; the guardrails README, all 8 core and 12 domain guardrails; the eight workflow guardrails not read before; all 28 files in `.agents/30-context`, 11 in `40-stubs`, 3 in `90-index`. Each agent reported the line count of every file it read and checked on disk every "does not exist" claim it made. The line ranges and percentages quoted for these files are theirs; I have not re-verified each range.
- **Read in full by me in a fourth pass:** `docs/failing-tests-workflow.md` (548 lines), `docs/test-first-workflow.md` (174), `docs/testing-guide.md` (243), `.github/workflows/main.yml` (180).
- **Not read beyond provenance:** the 38 Spec Kit and superpowers skills and the Spec Kit commands, agents and prompts. They are UPSTREAM whatever they contain.

No verdict is marked provisional any more.

One thing seen and left alone: after the fast-forward, `git status` shows `CLAUDE.md`, `AGENTS.md`, `.github/copilot-instructions.md` and three nested `AGENTS.md` files as modified. I did not touch them; `configure.ps1` (hardlink repair) is the likely cause.

---

## Reference: what would Bryan do?

Bryan Finster's [agentic-dev-team](https://github.com/bdfinst/agentic-dev-team) (MIT) is a
**reference only** for the plugin repository (NKDAEngineering-Delivery), not for this one; it is
recorded in that repository's notes. For each plugin design question (packaging, plugin split,
workflow, skills, evals), check what it does and say where we follow it and where we differ, and
why. It is never a dependency, and nothing is copied from it.

Where it already differs from this proposal:

- **Packaging:** one marketplace, three plugins. The foundation `dev-team` holds workflow and
  practice together; companions are split off only for a different audience (security, plugin
  authoring).
- **Workflow:** it owns a fixed `/specs → /plan → /build → /pr`, by LLM-driven orchestration
  (its ADR-0013). Ours holds no rule of its own; each repository's Definition of Workflow defines
  the states, and a deterministic stage-gate tool makes every shift.
- **Testing the plugins:** evals in CI (`agent-eval.yml`, `plugin-tests.yml`) and a structural
  audit (`marketplace-dev`). This proposal has Pester tests for the stage gate only.

---

## 1. The two-plugin picture

### What another user installs and gets

```text
/plugin install nkda-engineering-delivery --marketplace nkdAgility/NKDAEngineering-Delivery
/plugin install nkda-engineering-practice --marketplace nkdAgility/NKDAEngineering-Delivery
```

**nkda-engineering-delivery** (exists; grows)

| They get | From |
|---|---|
| The runner, the stage-gate tool, the hook (today) | the plugin |
| State labels that fit their existing labels, branching shifts, checks on the issue body and the pull request, parent and dependency checks, manager check-in, definition validator | P1–P7 |
| Create work items from their templates (draft, duplicate check, create with parent) | `nkda-backlog-item-create` |
| Assess readiness and size, raise decisions, break down into sub-issues | `nkda-backlog-item-prestart`, `nkda-backlog-groom-workflow.js` |
| Deliver one item per worktree and pull request, independent verify, merge gate computed in code | `nkda-backlog-deliver-workflow.js` |
| A completion protocol and five-part completion report | `completion-protocol.md`, the PR template |
| A Definition of Done validator that runs the gates their repository names | `nkda-core-definition-of-done` |
| Session evidence: schema, validator, CI job; maintainer-applied override labels | `50-evidence`, `validate-evidence.py`, `verify-maintainer-label.sh`, `governance.yml` |
| Issue templates for a work item and a decision | `.github/ISSUE_TEMPLATE` |

**nkda-engineering-practice** (new)

| They get | From |
|---|---|
| Five architecture perspective checks, a combined review, and a check-triage-fix workflow | six `nkda-archcheck-*` skills, `nkda-archcheck-workflow.js` |
| Plan, tasks and implementation architecture compliance gates | three `nkda-core-*-architecture-compliance` skills |
| The TDD safety net: assess, design, refresh, plan, implement, adjust, verify | `nkda-tddsn-*` skills, agents, prompts, skill-set, `nkd-tdd-assessment` |
| Test validity scoring, test promotion, test category tagging | `-test-validity`, `-test-promotion`, `nkda-testcategory-workflow.js` |
| Failing-test procedure, guardrail refactor, red-team spec review | `nkda-debugtests`, `nkda-refactor`, `-red-team-review` |
| Implementation-parity check across variants, observability contract, docs sync, documentation architecture | `connector-coverage-check`, `nkda-observability-contract`, `update-docs`, `nkda-archimprove-documentation` |
| Tests-first session (start, review, fix, end) | the session kit |
| Spec Kit gates: checklist and task-coverage enforcement, strict implement | `scripts/guardrails`, `strict-implement` |

**What each repository provides.** The plugins hold method; the repository holds its rules as data.

- `platform/development/definition-of-workflow.md` + `.json` (delivery; exists as a contract today).
- `platform/engineering/definition-of-practice.json` (practice; new, P13). It names the repository's own files and facts; it does not restate them.

**What this repository keeps.** The Constitution, the guardrails and context under `.agents/`, the L3 Definition of Done, the live test environment, the consent policy, CI for build and release. They become the data the two definition files point to.

**Upstream.** Spec Kit and superpowers are never copied. A plugin that uses them says so in its README and checks at run time that they are present (as `speckit-superb-check` already does for superpowers).

---

## 2. Plugin changes needed

### Delivery plugin (P1–P11 as in the first pass; summarised, unchanged in substance)

| # | File | Behaviour | Test |
|---|---|---|---|
| P1 | `tools/Test-StageGate.ps1` lines 88, 93–99, 291–294; `hooks/Deny-StateEdit.ps1` lines 57–58 | Honour `storage.labelPrefix` as given, including `""`; state is the one label that names a state in the definition; the hook protects those labels instead of the literal `state:` | "reads `ready` among `backlog-item` and `priority/high`"; "refuses `ready` + `blocked`"; hook "denies `--add-label ready`", "allows `--add-label priority/high`" |
| P2 | `Test-StageGate.ps1` lines 150, 175 | `storage.itemLabel`: `-List` and `-Package` see only issues carrying it | "`-List` returns only item-labelled issues"; "refuses a Decision as a package" |
| P3 | `workflows/engineering-delivery.js` (`GATE_FOR`, `runTool`, the loop) | Several shifts out of one state; the state's tool returns the shift to gate | new `tests/Runner.Tests.ps1` with stubbed `agent`: "gates the shift the tool returns"; "waits when none is returned"; "Hub fixture walks as before" |
| P4 | `Test-StageGate.ps1` (`Invoke-Checks`) | `source`: `comments` (default), `body` (read only), `pr`; check types `checklist` and `prState` | "checklist on the PR body reports the unticked step"; "prState merged not met for an open PR"; "body check writes nothing" |
| P5 | `Test-StageGate.ps1` | Check types `childrenClosed`, `dependenciesMerged`; `-List` reports `parent` | "names the open sub-issue"; "not met when the dependency's PR is open" |
| P6 | `Test-StageGate.ps1` (`-List`), runner | `statusUpdatedAt`; runner mode `{ "checkIn": true }` lists packages stale beyond `definition.checkInMinutes` | "lists a package 25 minutes stale at 20, not one at 5" |
| P7 | `Test-StageGate.ps1` (`-Validate`) | Reports unreachable states, unknown check types, and state or item labels missing on GitHub | "reports `in-progress`, whose label does not exist" |
| P8 | runner (`PLUGIN_TOOLS`) | `plugin:verify-and-gate`: independent reviewer, gate computed by the runner from `work[state].autoMerge.minConfidence` | "enables at 90"; "disables at 89"; "disables when the verifier returns nothing" |
| P9 | runner lines 128, 161–167, 178–182 | Hub skill, file and agent names move into `work[state]` (`rules`, `skills`, `reviewers`) | "Hub fixture produces the same prompts" |
| P10 | new `skills/create-work-item` | From `nkda-backlog-item-create`; templates and labels from the definition; created in dependency order so no description is edited | "orders parents, decisions, dependants"; "refuses a cycle" |
| P11 | `plugin.json`, new CI | Bump the version on every behaviour change (source and installed are both `0.1.0` and differ); run Pester in CI | CI fails when `plugins/` changes and `version` does not |

### Delivery plugin, new in this pass

**P12. Standing authorisation check.**
- File: `tools/Test-StageGate.ps1` (`-Apply`), runner.
- Behaviour: the definition may name `authorisation: { file, key, ref }` (here `.agents/10-contracts/consent-policy.yaml`, `standing_authorisations.backlog-automation`, `origin/main`). When present, an unattended run applies a shift or creates an issue only if that key exists at that ref; otherwise it reports and changes nothing. This is the check both backlog workflows repeat in prose (`nkda-backlog-groom-workflow.js` line 57, `nkda-backlog-deliver-workflow.js` line 96).
- Tests: "applies when the key is on origin/main"; "changes nothing and says why when it is only on a branch".

**P13. Skills and tools moved in from this repository.**
- `skills/assess-readiness` (from `nkda-backlog-item-prestart`): assess and apply modes, verdict returned as a shift (P3), Decisions raised and reused, breakdown into sub-issues with a coverage map, safe to re-run. The checks themselves are read from the file the definition names for the state.
- `skills/definition-of-done` (from `nkda-core-definition-of-done`): runs the gates listed in the definition (`dod.gates[]`: name, command, pass rule, rule file) and writes the report where the shift's check reads it.
- `skills/completion-protocol` (from `completion-protocol.md` §A–§C): the ordered steps come from `completion.steps[]`.
- `tools/Test-SessionEvidence` (from `validate-evidence.py`) with the schema; `tools/Test-MaintainerLabel` (from `verify-maintainer-label.sh`), reading `maintainer` from the definition.
- `templates/`: work-item, decision and pull-request templates with marked slots.
- A reusable GitHub workflow (`workflow_call`) for the evidence gate and the "contract change needs an ADR or a maintainer label" gate, taking path patterns and label names as inputs.
- Tests: each tool gets a Pester test with a fixture definition; the Hub and migration-tools fixtures still pass unchanged.

### Practice plugin scaffold

**P14. `plugins/nkda-engineering-practice`.**

```text
plugins/nkda-engineering-practice/
  .claude-plugin/plugin.json
  skills/        archcheck-* (6), arch-compliance-plan|tasks|implementation, tddsn-* (7),
                 test-validity, test-promotion, debug-tests, refactor, red-team-review,
                 variant-coverage, observability-contract, update-docs, documentation-architecture,
                 session-start|review|fix|end, strict-implement
  agents/        tddsn-reviewer, -test-architect, -implementation, -verification, -autonomous
  commands/      tddsn-autonomous
  workflows/     archcheck.js, testcategory.js
  tools/         Get-PracticeDefinition.ps1, Test-Checklists.ps1, Test-TaskCoverage.ps1, Test-Orchestrator.ps1
  reference/     tddsn/ (manifest, workflow, contracts, outputs, assessment-method)
  tests/         Pester, with one fixture repository
```

- Add the entry to `.claude-plugin/marketplace.json`.
- `Get-PracticeDefinition.ps1` reads `platform/engineering/definition-of-practice.json`, exits 2 with a plain message when it is missing, and prints the section a skill asks for. Every split skill starts by calling it; none carries a default.
- Tests: "refuses a repository with no definition"; "returns the architecture section"; "reports a section the definition lacks by name"; one test per skill that its text contains no `DevOpsMigrationPlatform`, `.agents/`, `net481`, `TFS` or `Simulated` (a grep over `skills/`, so repository text cannot creep back).

**P15. The data file a repository provides: `platform/engineering/definition-of-practice.json`.** Sections, each optional; a skill that needs a missing section stops and says which.

| Section | Holds | Used by |
|---|---|---|
| `rules` | Paths to the repository's own rule files: testing rules, coding standards, architecture boundaries, observability, definition of done, constitution | every skill that lists "Preconditions" |
| `architecture` | Root namespace; inner-ring project(s); module projects; infrastructure and host projects; store and state interfaces; SDK namespaces and concrete types that must not leak; the slices | archcheck skills and workflow, plan compliance, refactor |
| `variants` | The implementations every feature must cover (here Simulated, AzureDevOpsServices, TeamFoundationServer), the runtime of each, the exemption rule, registration name patterns | variant-coverage, plan compliance |
| `tests` | Category ladder with markers and speed budgets; dual-tag rule; test projects; commands for unit, simulated and live suites; evidence locations; extra waste patterns | test-promotion, test-validity, testcategory workflow, debug-tests, tddsn |
| `docs` | Change kind → document map; target documentation layout; ADR folder | update-docs, documentation-architecture, session-end |
| `observability` | Telemetry model document; files holding well-known metric, meter and activity-source names; metric domains | observability-contract |
| `runtime` | Target frameworks and the boundary between them | refactor, consolidation analysis |
| `speckit` | Required hooks and required artefacts | Test-Orchestrator, strict-implement |
| `changeClasses` | Path to the change-class and consent files | archcheck workflow triage, refactor |

---

## 3. Verdict tables

### 3.1 Skills (75)

**Share as it is → practice (11)**

| Skill | Reason |
|---|---|
| `nkda-archimprove-red-team-review` | Five lenses, severity table, report format; no repository name anywhere. Mentions Spec Kit's `after_specify` only. |
| `nkda-core-implementation-architecture-compliance` | 29 lines of generic checks (stubs, seam bypass, pass/fail per perspective). |
| `nkda-core-tasks-architecture-compliance` | Generic requirement-to-task coverage against `spec.md`, `plan.md`, `tasks.md`. |
| `nkda-tddsn-target-suite-design` | Generic; reads only the tddsn artefacts and manifest. |
| `nkda-tddsn-architecture-refresh` | Generic. |
| `nkda-tddsn-rebuild-planning` | Generic. |
| `nkda-tddsn-test-implementation` | Generic ("use MSTest unless the repository clearly uses a different framework"). |
| `nkda-tddsn-code-adjustment` | Generic minimal-change gate. |
| `nkda-tddsn-verification-review` | Generic; PowerShell test evidence. |
| `fix` | Generic resume table. Ships only with the agent definitions it names (open decision 8); rename, since `fix` is too general a name. |
| `review` | Generic. Same condition; rename, since it collides with the built-in `review`. |

The six tddsn skills name `.agents/skill-sets/nkda-tddsn/manifest.md`; that path becomes the plugin's `reference/tddsn/` when they move. That is packaging, not repository data.

**Share after split → delivery (3)**

| Skill | Repository-specific text | Becomes |
|---|---|---|
| `nkda-backlog-item-create` | `SKILL.md:8` template paths; `:14` `analysis/backlog/drafts`; `:39–40` connectors, phases, migration-tools mapping rows; `:45` "never use the word Epic"; `:60` label names and colours | `definition-of-workflow.json`: `storage.templates`, `storage.itemLabel`, `drafts` path; the wording rules stay in this repository's issue template |
| `nkda-backlog-item-prestart` | `SKILL.md:8` guardrail path; `:49–59` scope and agent-placement checks (csproj target frameworks, `TfsJobAgentWorker`, `QueueCommand.GetConnectors`, net481 rules); `:71` routing catalog; `:74` live E2E; `:76`, `:90` 2 days and 5 criteria | `work["needs-assessment"].rules` → `backlog-item-workflow.md` §2.1–2.2, which already holds these checks; the skill keeps the mechanics |
| `nkda-core-definition-of-done` | Full read: about 55% is this repository or its stack. `SKILL.md:39–42` guardrail paths; `:56–59`, `:190–208` gate list and connector gate; `:113`, `:128`, `:136–139`, `:171–179` `dotnet` commands and code-quality greps; `:152–153`, `:160` `scenarios/`; `:214–220` `docs/`, `build.ps1`, `launch.json`; `:231–236` `discrepancies.md`; report rows `:263–268`, `:313–326`, `:358–362`, `:385–394` | `dod.gates[]` in `definition-of-workflow.json`: name, command, forbidden patterns, pass rule, rule file. The gate list itself must be data; gates 3, 5 and 6 are wholly this repository's. Defects: `:137` greps `features/`, which does not exist; `:8` calls `definition-of-done.md` canonical and then restates seven gates |

**Share after split → practice (19)**

| Skill | Repository-specific text | Becomes |
|---|---|---|
| `nkda-archcheck-clean-architecture` | `:22–37` ring diagram names; `:47`, `:196` `DevOpsMigrationPlatform.Abstractions`; grep blocks `:70–72`, `:114–118`, `:155–158`, `:186–189`, `:209–213`; all examples | `architecture`: inner ring, outer-ring projects, leaking types |
| `nkda-archcheck-hexagonal` | `:22–34` boundary diagram; `:43` store types; grep blocks `:76–79`, `:120–124`, `:144–148`, `:182–186`, `:208–210`, `:239–242`, `:267–271`, `:301–303`; `:256` `IProgressSink` | `architecture`: stores, SDK types, hosts allowed to use Console and environment |
| `nkda-archcheck-modular-monolith` | `:22–34` diagram; `:37` `IArtefactStore`/`IStateStore`; grep blocks `:55–57`, `:96–100`, `:122–130`, `:168–170` | `architecture`: module projects, shared-contract project, cross-cutting services |
| `nkda-archcheck-screaming-architecture` | `:30–32`, `:89–101` project names; `:110` business nouns; `:151`, `:170` `DomainTests` category; `:203–205` | `architecture`: root namespace, business nouns, boundary terms; `tests`: behavioural category |
| `nkda-archcheck-vertical-slice` | `:20–30` slice model; `:65–68`, `:94–96`; `:107–114` `SystemTest`; `:123–146` `IStateStore` key rule; `:152–167` `Testing.Dsl`, `Features/` | `architecture.slices`, state-key rule; `tests`: system and scenario categories |
| `nkda-archcheck-architecture-review` | Key questions `:46`, `:55`, `:63–64`, `:73`, `:81`; every example in `:128–198` | Examples replaced by placeholders; questions read from `architecture` |
| `nkda-core-plan-architecture-compliance` | Full read: about 45% repository. `:23–30`, `:50–53` harness and constitution paths; `:82–91`, `:100–109`, `:116–128`, `:137–146` pass criteria; `:169–178` constitution principles; `:184–195` numbered boundary rules; examples `:236–254`; `:302–304` `/speckit.*` | `rules.constitution`, `rules.architectureBoundaries`; `architecture`; `variants`; `speckit`. It has drifted from the files it cites: `:172`, `:174` give the wrong principle numbers; `:186` says 23 boundary rules where the file has 30; `:189` describes rule 7 as `IArtefactStore`, now `IPackageAccess` |
| `connector-coverage-check` | Full read: about 40% repository. `:39–43` paths; `:49–64` the three connectors and the TFS exemption rule; `:85–89`, `:93–97` feature taxonomy and `AddSimulated*`/`AddAzureDevOps*`/`AddTfs*`; `:114–117`, `:132–134`, `:141`, `:150`, `:171`; report columns `:201–217`, `:245–247`, `:257–259`, `:292–299` | `variants`: list, always-required flags, runtimes, exemption rule, abstraction names, stub patterns. Shared as a variant-coverage check. Defects: `:55` says TFS import "returns `Task.CompletedTask`", against Constitution rule 10; `:61` `EventKind.Warning` has no hit in `src/`; `.specify/extensions.yml:22` passes `--stage spec`, which the skill does not define |
| `nkda-observability-contract` | Full read: about 25% repository or stack; the most general of the long skills. `:59–61` O-1..O-4 and `IProgressSink.EmitAsync`; `:74–76`; `:111–116` telemetry model and name files; `:185`, `:223–226`, `:245`, `:252`, `:272–274`, `:314–315`, `:340–341`; KQL at `:363`, `:377–382` | `observability`: naming scheme, domains, required tags, name registries, query language. Defects: `:112` `WellKnownMetricNames.cs` does not exist (the files are `WellKnownAgent/Cli/ControlPlaneMetricNames.cs`); `IMigrationMetrics`, `IDiscoveryMetrics`, `IJob`, `ICommandHandler` have no hit in `src/`; `:608–610` lists document-mode criteria under "Codebase mode only" |
| `update-docs` | Full read: about 55% repository. `:36–41` harness and `specs/` paths; `:53–72` the change → document table; `:89–93`, `:99–104` enumerations; `:137–142`; `:144–164` two steps wholly about named files; `:173–177`, `:193–195` | `docs.map`, `docs.enumerations`, canonical reference files. Defect: `:193–195` quotes a "Mandatory Compliance Review Loop" from `agents.md` that is not there |
| `nkda-archimprove-documentation` | Full read. `SKILL.md` about 30% repository (`:12–25`, `:33–41`, `:51–53`, `:77–85`, `:104–111`, `:133–136`, `:171–173`, `:181–188`, `:215–221`); `DOCUMENTATION-MAP.md:9–204` is data throughout; `PLACEMENT-RULES.md:9–14`, `:32–35`, `:107–162`, `:174–190`; `REPORT-FORMAT.md:12–25`, `:46–47`, `:94–100`; `TOKEN-BUDGETS.md` folder names only; `ADR-FORMAT.md:16–20` | `docs.layout`, audiences, target map. `DOCUMENTATION-MAP.md` moves whole into repository data and is stale: `:29–66` uses `.agents/guardrails/` and `.agents/context/`, which do not exist, and lists ADRs 0001–0013 of 34. `SKILL.md:183` cites an `agents.md` heading that does not exist |
| `nkda-archimprove-test-promotion` | `:27–32` and `:51–56` category markers and speed budgets; `:40–43` guardrail paths; `:65` Simulated connector; `:131–132` `dotnet` | `tests`: ladder, markers, commands; `rules` |
| `nkda-archimprove-test-validity` | `:29–30` guardrail paths; `:105` test glob; `:188–189` `dotnet`; `:240–255` module and connector waste patterns (`IArtefactStore`, `Simulated*Target`) | `tests.extraWastePatterns`, commands; `rules` |
| `nkda-debugtests` | `:22` and `:245–248` guardrail and skill paths; `:64–74` and `:206–217` `errors.json`, `.otel-diagnostics`, the four process logs; `:131–133` `build.ps1` commands; suite names in steps 3, 5, 6 | `tests`: suites in widening order, commands, evidence locations |
| `nkda-refactor` | `:49–64` harness, contract and guardrail paths; `:75–77` and `:131` cross-TFM duplication check (`*.net481.*`/`*.net10.*`); `:78–80` change classes | `rules`, `changeClasses`, `runtime` |
| `nkda-tddsn-assessment` | `:10`, `:20`, `:24`, `:41` dependence on `nkd-tdd-assessment`; `:46–50` guardrail paths | `rules`; the method moves into `reference/tddsn/assessment-method.md` |
| `nkd-tdd-assessment` | `:48–49`, `:84` `.agents/30-context/architecture/`; `:79–83` guardrail and `docs/testing-guide.md` paths; `:88–93` example documents; `:662` priority wording (package contract, migration output) | Merged into the tddsn assessment as its reference method; `rules`, subsystem-document folder |
| `start-session` | `:18–21` links to `copilot-instructions.md`, two guardrails and `features/`; `:25` `Logs/atdd-sessions` | `rules`; evidence folder from the delivery definition |
| `end-session` | `:17–27` the documentation-sync checklist (ten lines of this repository's documents); `:31–35` schema and validator paths; `:43` change classes; `:52–54` suite names | `docs.map`; evidence schema and suites from the delivery definition |

**Upstream (39)**

- Superpowers (14): `brainstorming`, `dispatching-parallel-agents`, `executing-plans`, `finishing-a-development-branch`, `receiving-code-review`, `requesting-code-review`, `subagent-driven-development`, `systematic-debugging`, `test-driven-development`, `using-git-worktrees`, `using-superpowers`, `verification-before-completion`, `writing-plans`, `writing-skills`.
- Spec Kit 1.1.2 (24): `speckit-agent-context-update`, `-analyze`, `-checklist`, `-clarify`, `-constitution`, `-converge`, `-git-commit`, `-git-feature`, `-git-initialize`, `-git-remote`, `-git-validate`, `-implement`, `-plan`, `-specify`, `-tasks`, `-taskstoissues`, and eight `speckit-superb-*`.
- `nkda-archimprove-codebase` (1): its four files read as a renamed copy of a published "improve codebase architecture" skill (generic examples such as PGLite, Stripe and "Order intake"; no repository name). The only local edit is `SKILL.md:68` and `:70`, which link to `../nkda-archimprove-documentation`. Confirm the source (open decision 7); until then it is not copied into a plugin.

**Remove (3)**

| Skill | Evidence |
|---|---|
| `session-hooks` | `SKILL.md:26` writes `Logs/atdd-sessions/<id>.log`; the evidence CI accepts is `.json` (`end-session:31–35`, `governance.yml` matches `Logs/atdd-sessions/.*\.json`). `:36–37` has two conflicting `Phase:` lines. Hook 6 (`:84–94`) posts "a signal to CI" that nothing receives. |
| `source-command-nkda-tddsn-autonomous` | Lines 12–42 repeat `.agents/commands/nkda-tddsn-autonomous.md` word for word. |
| `source-command-strict-implement` | Lines 12–72 repeat `.agents/commands/strict-implement.md` word for word. |

### 3.2 Commands, agents, prompts, skill-set, links

| Item | Verdict | Reason or split |
|---|---|---|
| `commands/nkda-tddsn-autonomous.md` | SHARE-PRACTICE | 35 generic lines; the tddsn entry point. |
| `commands/strict-implement.md` | SHARE AFTER SPLIT → practice | `:19`, `:30`, `:35` call `scripts/guardrails/*.ps1` (become plugin tools); `:40–42`, `:52–54` the gate list → `speckit.gates`. |
| `commands/nkda-core-implementation-architecture-compliance.md`, `commands/nkda-core-tasks-architecture-compliance.md` | REMOVE (2) | Each is a 20-line restatement of the skill of the same name. They exist because `validate-orchestrator.ps1:146–147` requires them; change that list when the skills move. |
| Spec Kit commands (14) | UPSTREAM | `speckit.agent-context.update`, five `speckit.git.*`, eight `speckit.superb.*`. |
| `agents/nkda-tddsn-reviewer.md`, `-test-architect.md`, `-implementation.md`, `-verification.md` | SHARE-PRACTICE (4) | Role, allowed skills, allowed changes; generic. |
| `agents/nkda-tddsn-autonomous.md` | SHARE AFTER SPLIT → practice | `:35–40` five guardrail paths and the legacy skill path → `rules`; `:69` `dotnet test` → `tests` command. |
| Spec Kit agents (24) | UPSTREAM | Copilot integration files. |
| `prompts/nkda-tddsn-assess`, `-design`, `-rebuild`, `-verify`, `-autonomous` (5) | SHARE-PRACTICE | Generic entry points for tools that read prompts rather than skills. |
| `prompts/consolidation-analysis.prompt.md` | SHARE AFTER SPLIT → practice | `:35–46` net481/net10 topology and the TfsObjectModel constraint; `:120–121`, `:203` → `runtime`. The detection heuristics and report format are generic. |
| Spec Kit prompts (24) | UPSTREAM | |
| `.agents/skill-sets/nkda-tddsn` (5 files) | SHARE AFTER SPLIT → practice | `manifest.md:64–68` guardrail paths → `rules`; `README.md:43–49` this repository's subsystem names → examples removed. Defect to fix on the way: `manifest.md:29–32` lists four commands under `.agents/commands/` that exist only as prompts. |
| `.claude/skills`, `agents`, `commands`, `prompts`, `workflows`; `.github/agents`, `prompts` | REPO-ONLY | Seven symlinks into `.agents/`; this repository's wiring, not content. |

### 3.3 Workflows

| Workflow | Verdict | Repository-specific text → becomes |
|---|---|---|
| `nkda-backlog-groom-workflow.js` | SHARE AFTER SPLIT → delivery | `:7`, `:52` skill path → `work[state].tool`; `:57` authorisation check → P12; `:69` label names → state and item labels (P1, P2); `:9`, `:114`, `:134` report path → `reports` path. The select / assess / apply / recurse loop becomes the runner. |
| `nkda-backlog-deliver-workflow.js` | SHARE AFTER SPLIT → delivery | `:21–26` one slot until #176 → `wipLimit`; `:71` eligibility text → shift checks (P5); `:83` area rule → `pullOrder` / exclusion; `:96` → P12; `:100` branch pattern; `:101–106` live environment, test-first file, completion protocol, co-author line, `adpe2e-` cleanup → `work[state].rules` and `completion.steps[]`; `:113–114` DoD file and evidence validator; `:122` the 90 → `autoMerge.minConfidence` (P8). |
| `nkda-archcheck-workflow.js` | SHARE AFTER SPLIT → practice (confirmed on full read) | `:11–14`, `:167–244` four compliance phases reading this repository's module, orchestrator, connector and field-transform contracts → `architecture.compliance[]` (name + files); `:249`, `:318`, `:346–360` the fixed ten-perspective list; scope `src/` and `features/` at `:90`–`:235`; `:309–317` change-class files and Class A/B/C wording → `changeClasses`; `:541–545`, `:608–609` `dotnet` commands → `tests`; `analysis/archcheck/` paths → `reports`. Check → triage → batch fix → verify → report is generic. It never commits (`:504`, `:653`). `:544` and `:612` revert a failed fix with `git checkout -- <file>`, limited to the files the batch itself modified; the remaining risk is narrow: uncommitted operator edits inside those same files are discarded with the fix. Say so in the skill text, or stash per file, before sharing. Other defects: `features/` does not exist; `:28` says other arguments run everything, `:30–32` make them run nothing; `:705` asks for a commit SHA that cannot exist. |
| `nkda-testcategory-workflow.js` | SHARE AFTER SPLIT → practice (confirmed, weakly) | Only the skeleton (discover per project, classify per file, write) is generic. `:13–103` the category rules → `tests` taxonomy; `:108–119` test projects → `tests.projects` (the list omits `Infrastructure.AzureDevOps.Tests`, which exists); `:121`, `:165`, `:228` an absolute user path. `:230–231` run `git add` and `git commit` per file and `:235` hardcodes a co-author line; Constitution rule 11 forbids that here and the standing authorisation does not cover this workflow. Share only after committing is removed or made opt-in. |
| `.specify/workflows/speckit/workflow.yml` | UPSTREAM | |

### 3.4 Setup

| Item | Verdict | Reason or split |
|---|---|---|
| `CLAUDE.md` / `AGENTS.md` / `.agents/agents.md` / `.github/copilot-instructions.md` (one blob) | REPO-ONLY | The Constitution and the routing rule are this repository's rules. Needs one added line pointing at the two definition files. |
| `.agents/00-entry/manifest.yaml`, `task-profiles.yaml`; `10-contracts/routing-catalog.yaml`, `change-classes.yaml`, `seam-catalog.yaml`, `surface-catalog.yaml`, `specs/` | REPO-ONLY | Their content is the repository's rules as data; the definition files point at them. |
| `.agents/10-contracts/schemas/` (6 files) | SHARE-PRACTICE (changed) | Full read: structural only, no product vocabulary. Their only fixed values are harness conventions: the `^\.agents/` path pattern (seam-catalog `:18`, surface-catalog `:19`, task-profiles `:30`), exactly three change classes (change-classes `:11`), and fail-closed constants. They move with the harness (open decision 6). |
| `.agents/00-entry/reading-order.md` | SHARE AFTER SPLIT → practice | `:2–8` the generic loading order; `:3`, `:12–16` name this repository's guardrail files → `rules`. `:3` says read `taxonomy-naming.md` first, while `CLAUDE.md` starts at `manifest.yaml`. |
| `scripts/guardrails/validate-agent-contracts.py` | SHARE AFTER SPLIT → practice | Checks 2–5 are generic. `:30–39`, `:67–68` catalog paths; `:102–105` `docs/adr` and the ADR digest path and heading; `:112–114` index and domains paths → a `harness` section. `:105`, `:112` read files unguarded. |
| `.agents/10-contracts/consent-policy.yaml` | REPO-ONLY | This repository's consents. P12 lets the plugin check it. Its `scope` names two workflow files and two skills by path and must be re-scoped before they move (open decision 4). |
| `.agents/20-guardrails/README.md` | REPO-ONLY | An index of this repository's guardrail files. |
| `core/architecture-perspectives-ethos.md` | SHARE-PRACTICE (changed) | Full read: no product text in 151 lines. The six perspectives with invariants, evidence and reject conditions; it is the rule text the archcheck skills enforce. |
| `core/surface-usage.md` | SHARE-PRACTICE (changed) | No product text; `:7–11` repeat `capability-ethos-rules.md:22–26`. Merge the two when they move. |
| `core/capability-ethos-rules.md` | SHARE AFTER SPLIT → practice (changed) | About 90% generic. Stays: `:28` `DevOpsMigrationPlatform.Abstractions*`; `:68`, `:70` `IModuleExtension`; `:92` ADR-0017. |
| `core/coding-standards.md` | SHARE AFTER SPLIT → practice (changed) | About half generic (`:16–25`, `:44–45`, `:51–56`, `:59–63` and single lines). Stays: `:29–33`, `:37–40`, `:46–49`, `:57`, `:64`, `:69`, `:71`, `:76`, `:78–79`, `:84–88`. |
| `core/runtime-compatibility-net10-net481.md` | SHARE AFTER SPLIT → practice (changed, borderline) | Every rule names net481 or net10, so nothing moves as written. The multi-target method (`:21–30`, `:34–42`, `:46–53`, `:82–87`) is reusable once the framework names become `runtime` data. Stays: `:16`, `:22`, `:49`, `:91–93` (full-parity policy, TFS Object Model). |
| `core/taxonomy-naming.md` | SHARE AFTER SPLIT → practice (changed) | About 55% generic: role definitions, distinctions and naming rules (`:19–29`, `:99–109`, `:119–140`). `:144–154` (session operator) belongs to delivery. Stays: `:7–17`, `:31–38`, `:44–52`, `:60–63`, `:95–97`, `:111–117`, `:156–161` (named maintainer). |
| `core/change-governance.md` | SHARE AFTER SPLIT → delivery (changed) | About 80% generic: change classes A/B/C, the consent gate, the commit rule (`:1–24`, `:35–50`). Stays: `:26–33` (governance job, `src/*Abstractions*`, ADR-0031, the OWNERS script). |
| `core/architecture-boundaries.md` | REPO-ONLY (confirmed) | 30 numbered product rules. The three generic lines (`:7`, `:63`, `:79`) are repeated in files that do move. |
| `domains/configuration-rules.md` | SHARE AFTER SPLIT → practice (changed) | About half generic (`:7–8`, `:13`, `:24–25`, `:30–31`). Stays: `:9`, `:14`, `:18–20`, `:29`, `:35`, `:39–41`. |
| `domains/observability-requirements.md` | SHARE AFTER SPLIT → practice (changed) | About 45% generic: `:7–11`, `:13`, `:19–23` (attempt / completion / error / duration / in-flight), `:29–36`, `:64–73`. Stays: `:12`, `:17`, `:25`, `:38–62`, `:74–81`, `:85–87`. Pairs with `nkda-observability-contract`. |
| `domains/security-rules.md` | SHARE AFTER SPLIT → practice (changed) | About 55% generic (`:7`, `:9–10`, `:16`, `:20`, `:22`, `:26`). Stays: `:8`, `:14–15`, `:21`, `:27`, `:31–32`, `:36–38`. |
| `domains/blocking-errors.md`, `cli-tui-rules.md`, `connector-rules.md`, `control-plane-rules.md`, `data-sovereignty-rules.md`, `migration-rules.md`, `module-rules.md`, `package-rules.md`, `workitems-rules.md` (9) | REPO-ONLY (confirmed) | Full read: between 0% and 30% generic each, and the generic lines are repeated in files that do move. They are the package, connector, control-plane and work-item rules of this product. |
| `20-guardrails/workflow/backlog-item-workflow.md` | SHARE AFTER SPLIT → delivery | §1 (`:21–33` one task = one PR = one worktree, manager, 20 minutes) and §2.3–2.5 (`:57–90` verdict labels, breakdown, parents) are mechanics → plugin skill text and the definition (`checkInMinutes`, shifts). §2.1–2.2 (`:38–56`) stay as this repository's readiness rules. |
| `20-guardrails/workflow/completion-protocol.md` | SHARE AFTER SPLIT → delivery | Steps 0, 2, 5, 6, the report (§B) and the verifier rule (§C) are generic. `:15–16` `dotnet build`, `build.ps1 install`; `:20–23` live migration and TFS; `:26` the compliance skill; `:46` `TestCategory`; `:70` consent file → `completion.steps[]`. |
| `20-guardrails/workflow/backlog-item-dod.md` | REPO-ONLY | C2–C5 are connectors, live proof, migration-tools parity and operator documents. It is the data `dod` points at. |
| `20-guardrails/workflow/live-test-environment.md` | REPO-ONLY | Organisations, the Entra test app, `adpe2e-` prefix, environment variables. |
| `20-guardrails/workflow/definition-of-done.md` | SHARE AFTER SPLIT → delivery (changed) | Full read: about 40% generic (`:15–23`, `:47–50`, `:54–61`, `:79–92`, `:96–100`, `:116–123`). Stays as gate data: `:9–11`, `:25–43`, `:65–77`, `:102–112`, most of the checklist `:130–156`. `:27` says "all four checks" over a six-row table. |
| `docs/failing-tests-workflow.md` | SHARE AFTER SPLIT → practice (added) | The method lives here, and about 85% of it is generic: the iron law (`:12–24`), the completion gate (`:28–56`), evidence freshness (`:60–77`), the ledger (`:81–104`), invalidation (`:108–151`), steps 1–7 with the root-cause format and the one-fix rule (`:155–481`), timeout-increase conditions (`:428–442`), red flags (`:500–513`), skipped-is-not-passed (`:517–539`). Repository text → `tests`: the `build.ps1` commands (`:94–96`, `:160`, `:200`, `:314`, `:335`, `:364`, `:387`, `:416`); the three gate names unit / simulated / live (`:34–39`, `:50–52`); the evidence sources (`:223–248`, `:485–496`: `errors.json`, per-process OTel logs, package output); `:533` the TFS example. Becomes `reference/debug-tests/procedure.md` in the practice plugin. |
| `20-guardrails/workflow/failing-tests-workflow.md` | SHARE AFTER SPLIT → practice (back from delivery) | A pointer to the doc above: its gate text repeats doc `:12–56` and `:60–77`, and `nkda-debugtests` repeats the same again. The doc shows the completion gate is step 7 of the debugging procedure, not a separate delivery rule, so the three copies merge into one practice skill with one reference. Delivery's completion protocol (step 2, "run the full suite fresh after your last change") cites it. What stays here: `:3–6`, `:21–23` the suite commands, `:33`, `:37–38`. |
| `docs/test-first-workflow.md` | SHARE AFTER SPLIT → practice (added) | The session model lives here; about 65% generic: the core principle and RED → GREEN → REFACTOR with widening proofs (`:14–24`, `:46–54`), the phase table (`:60–70`), handoff rules (`:74–83`), the session-log format (`:87–132`), preconditions, boundaries and adoption (`:136–165`). Repository text: `:44`, `:66` the perspectives guardrail path; `:67` the DSL project and test path; `:89` `Logs/atdd-sessions`; `:139` the branch example → `rules`, `tests`, evidence folder. The skill and command paths at `:29–31`, `:56`, `:65`, `:68`, `:80`, `:142–145` stop being repository text once those skills are in the plugin. Two things it settles: the phase owners are existing skills and commands (`speckit.specify`, the three hardening skills, `nkda-tddsn-autonomous`, the review skill), not the Specification / Implementation / Reviewer agents the session skills name; and it logs to `<session-id>.md` (`:89`), where `end-session` writes `.json` and `session-hooks` writes `.log`. |
| `20-guardrails/workflow/test-first-workflow.md` | SHARE AFTER SPLIT → practice (confirmed) | A pointer to the doc above: its 14 rules are the doc's handoff rules as reject conditions. It moves as the rule list beside the doc. Stays: `:3–5`, `:13–15`, `:19`, `:22`, `:30`, `:36–39`. |
| `docs/testing-guide.md` | SHARE AFTER SPLIT → practice (added) | About 30% generic, and that part is what the test-quality skills need as method: "speed budget is not a classifier" (`:56`), the selection rule "push tests downward" (`:70–80`), conventions and naming (`:123–133`), lifecycle-test rules (`:226–233`). `:7–36` repeats `docs/test-first-workflow.md`. Stays as `tests` data: the hierarchy and distinguishing tables (`:40–68`), the commands (`:86–117`), the DSL layout (`:139–150`), simulated and module expectations (`:152–172`), diagnostics (`:180–200`), test data and assertion standards (`:202–214`), the SC-004 plan (`:235–242`). Defects: `:84` requires tags on every `[TestClass]`, while `nkda-testcategory-workflow.js:90–93` deletes them as "always wrong"; `:185` gives the diagnostics folder without the `-<ts>` suffix the failing-tests doc uses. |
| `20-guardrails/workflow/testing-rules.md` | SHARE AFTER SPLIT → practice (changed) | About 40% generic: framework, mock and prohibited-pattern rules (`:13`, `:27–41`, `:48`, `:71–75`). Stays as `tests` data: `:14–25` the category table, `:45–47`, `:52–67`, `:77–92`. `:73` gives rules for `[Binding]` classes although `:47` says Reqnroll is retired. The category table is also in `docs/testing-guide.md:47–54`; keep one copy as the `tests` data. |
| `20-guardrails/workflow/delivery-quality-rules.md` | SHARE AFTER SPLIT → delivery (changed) | About 80% generic, and almost wholly a summary of other files. Stays: `:14`, `:17`, `:30–33`, `:43`. |
| `20-guardrails/workflow/spec-coverage-completeness.md` | SHARE AFTER SPLIT → delivery (changed) | About 88% generic: every requirement is covered before done. Stays: `:12–16` (the `IControlPlaneClient` incident), `:23`. |
| `20-guardrails/workflow/documentation-rules.md` | SHARE AFTER SPLIT → practice (changed) | No product text; `:14`, `:19–24` are folder paths → `docs.layout`. A thin pointer to the documentation skill's `PLACEMENT-RULES.md` and `TOKEN-BUDGETS.md`. |
| `20-guardrails/workflow/engineering-nonfunctional-rules.md` | SHARE AFTER SPLIT → practice (changed) | About 80% generic across configuration, API design, resilience, security, performance and operations. Stays: `:8–9`, `:11`, `:23–24`, `:48`, `:60–63`. |
| `.agents/30-context/README.md` | SHARE AFTER SPLIT → practice (changed) | Generic layer convention (`:5` "context explains; it does not override guardrails", `:9–11` layout); `:15`, `:19` path pointers stay. Moves with the harness. |
| `.agents/30-context/architecture/module-anatomy.md` | REMOVE (changed) | A 13-line redirect; `:3` says "now architecture context only" and it holds no content. `architecture/readme.md:54` already carries the pointer. |
| `.agents/30-context`, the other 26 files | REPO-ONLY (confirmed) | Full read: compressed product knowledge (package, connectors, control plane, work items, the ADR digest). |
| `.agents/40-stubs/README.md` | SHARE AFTER SPLIT → practice (changed) | `:1–18` how to write a directory-local stub (blocking rules only, under about 25 lines, update the map in the same change) is method; `:20–32` the exempt-projects table stays. Moves with the harness. |
| `.agents/40-stubs`, the 10 stubs | REPO-ONLY (confirmed) | Product blocking rules per folder. They share one shape, which the README above describes. |
| `.agents/90-index` (3 files) | REPO-ONLY (confirmed) | Path lists. `contracts-index.md` omits `specs/orchestrator-contract.md`, which exists. |
| `.agents/50-evidence` (schema, README), `scripts/guardrails/validate-evidence.py` | SHARE AFTER SPLIT → delivery (confirmed) | `validate-evidence.py:36–37` schema and `Logs/atdd-sessions` paths → `evidence: { schema, dir }`. The schema is not generic as it stands: `:35` the class enum `A/B/C/docs-only`; `:61–68` the six suite names; `:36`, `:45` `.agents/` paths → from the definition. README `:38` says `nkda-core-definition-of-done` checks the file; that skill never mentions it. |
| `.agents/configure.ps1`, `.githooks/post-checkout`, `post-merge` | SHARE AFTER SPLIT → practice (with the harness, open decision 6) | `configure.ps1:172–174` the three hardlinks and `:183–210` the stub-to-folder table → a links list in the repository. The hooks only call the script. |
| `.agents/OWNERS` | REPO-ONLY | Names the maintainer; becomes `maintainer` in the delivery definition. |
| `.claude/settings.json` | REPO-ONLY | Add the marketplace and enable both plugins. |
| `.github/workflows/main.yml`, `copilot-setup-steps.yml` | REPO-ONLY (confirmed) | `main.yml` read in full (180 lines): one Windows build job for the net481 targets, GitVersion, `build.ps1` modes, a check that `SchemaGenerator` references the three connectors (`:58–65`), the Entra sign-in and live suite against this product's test organisation (`:79–106`), and release packaging of `MigrationTools-*.zip`. No method that is not already in the delivery definition; the suite order it runs (unit, smoke, simulated, live) is the `tests` data. |
| `.github/workflows/governance.yml` | SHARE AFTER SPLIT → delivery (widened) | Full read: all five jobs are generic mechanics with this repository's values. `secret-scan` (`:17–38`) has nothing repository-specific and moves as it is (delivery). `session-evidence` (`:103–161`) and `contract-adr-gate` (`:163–212`) → delivery; inputs: `:131` `^(src\|tests)/`, `:132` evidence path, `:188` the Abstractions and contracts pattern, `:189` `^docs/adr/`, `:154–156`, `:205–207` label names. `spec-guardrails` (`:55–101`) → practice for any Spec Kit repository; inputs `:83`, `:89`, `:96`. `contract-schemas` (`:40–53`) → practice with the harness. A reusable workflow needs the scripts inlined or a second checkout of the plugin repository, because the jobs call scripts from the caller's tree. |
| `.github/ISSUE_TEMPLATE/backlog-item.md` | SHARE AFTER SPLIT → delivery | Outcome / Why / Acceptance criteria / Links / Pre-start / Definition of Done is the skeleton. `:25–27` connectors, phases, agents; `:30` migration-tools table; `:57`, `:69` live E2E; `:95–98`, `:102` L3 lines → this repository's fill-in of the template slots. |
| `.github/ISSUE_TEMPLATE/decision.md` | SHARE AFTER SPLIT → delivery | Generic except `:3` "Constitution exemption" and `:14` "Constitution rule … Migration Tools behaviour" → neutral wording. |
| `.github/PULL_REQUEST_TEMPLATE.md` | SHARE AFTER SPLIT → delivery | The five-part report and the seven-step checklist are generic. `:1–2`, `:11`, `:16` harness paths; `:20–24` suite rows; `:37` "end-to-end migration" → slots filled from `completion.steps[]` and `tests`. |
| `.github/CODEOWNERS`, `rulesets/main-branch-ruleset.json`, `github-app.yml` | REPO-ONLY | Repository settings. The ruleset requires a code-owner approval, so an armed auto-merge still waits for Martin. `github-app.yml` is three lines (`automation: auto_issue_session: true, remote_control: true`): an app setting with no method in it. |
| `.specify` (Spec Kit 1.1.2) | UPSTREAM | `extensions.yml` is this repository's wiring and must be edited when hook commands move into a plugin namespace (open decision 5). `.registry` lists a `ralph` extension with no folder. |
| `.codex/config.toml`, `.copilot/config.json` | REPO-ONLY | Two-line tool settings. |
| `scripts/guardrails/enforce-checklists.ps1`, `enforce-task-coverage.ps1` | SHARE-PRACTICE (2) | Generic Spec Kit gates over a feature folder; no repository name. |
| `scripts/guardrails/validate-orchestrator.ps1` | SHARE AFTER SPLIT → practice (confirmed) | `:86`, `:91` local command and skill folders; `:102–106`, `:132` required hooks; `:144–150` required artefacts → `speckit`. Fragile: `:122`, `:126`, `:134` throw under strict mode when a hook item lacks `enabled` or `optional`. |
| `scripts/guardrails/verify-maintainer-label.sh` | SHARE AFTER SPLIT → delivery | `:17` reads the handle from `.agents/OWNERS`; `:6`, `:31` cite it → `maintainer` in the delivery definition. |
| GitHub labels | REPO-ONLY | `in-progress`, `class-c-approved`, `evidence-waived` are used and do not exist. P7 catches this. |

### Counts

| Verdict | Skills | Commands, agents, prompts, skill-set, links | Workflows | Setup rows | Total |
|---|---|---|---|---|---|
| SHARE-DELIVERY | 0 | 0 | 0 | 0 | 0 |
| SHARE-PRACTICE | 11 | 10 | 0 | 4 | 25 |
| SHARE AFTER SPLIT → delivery | 3 | 0 | 2 | 12 | 17 |
| SHARE AFTER SPLIT → practice | 19 | 4 | 2 | 21 | 46 |
| REPO-ONLY | 0 | 1 (7 links) | 0 | 25 | 26 |
| REMOVE | 3 | 2 | 0 | 1 | 6 |
| UPSTREAM | 39 | 62 | 1 | 1 | 103 |

Setup now has 64 rows because the guardrails are listed one by one and three `docs/` files were added. A row may cover several files (the nine product domain guardrails are one row; the 26 context files are one row).

No whole file goes to the delivery plugin as it is; the one exception inside a file is the `secret-scan` job of `governance.yml`.

### What the full read changed

- **18 verdicts moved from REPO-ONLY to shared.** Two guardrails need no change (`architecture-perspectives-ethos.md`, `surface-usage.md`); 14 guardrails and two READMEs split. The six schemas moved from split to share-as-is, and `module-anatomy.md` moved to REMOVE.
- **The fourth pass added three `docs/` files, all split to practice, and put the failing-tests guardrail back in practice.** For failing tests and for the tests-first session the method is in the doc and the guardrail is a pointer; `nkda-debugtests` is a third copy of the failing-tests procedure. Each set moves as one skill with one reference file. The guardrails were judged by name in the second pass, and the names misled: most hold general engineering rules with this product's examples.
- **9 domain guardrails, `architecture-boundaries.md`, 26 context files, 10 stubs and 3 indexes were confirmed REPO-ONLY** on a full read.
- **The split skills are more tied to this repository than the scan showed.** `nkda-core-definition-of-done` and `update-docs` are about 55% repository text, `connector-coverage-check` 40%, `nkda-core-plan-architecture-compliance` 45%. `nkda-observability-contract` is the cleanest at 25%.
- **The long skills have drifted from the rules they restate.** Wrong constitution principle numbers, a rule count of 23 against 30, type names with no hit in `src/`, an `agents.md` section that does not exist, a documentation map that describes a folder layout the repository no longer has. Moving the rules into data and leaving only method in the skill removes the second copy that drifts.
- **The same rule is written in several guardrails.** One-seam rules in five files, touched-scope remediation in three, O-1..O-5 in three, no-stubs in four. When the generic guardrails move, merge them first.
- **Product context contradicts itself in places** (telemetry endpoints against ADR-0020 in four context files and `cli-tui-rules.md:7–10`; tool purity; cursor and `plan.json` locations; O-numbering). These are this repository's to fix and do not affect sharing, but an agent reading them gets two answers.

---

## 4. Removals

| Item | Evidence |
|---|---|
| `skills/session-hooks` | Writes `.log`; CI accepts `.json`; a CI signal with no receiver; two conflicting phase lists. |
| `skills/source-command-nkda-tddsn-autonomous` | Verbatim copy of the command file. |
| `skills/source-command-strict-implement` | Verbatim copy of the command file. |
| `commands/nkda-core-implementation-architecture-compliance.md` | Restates the skill of the same name; kept alive only by `validate-orchestrator.ps1:147`. |
| `commands/nkda-core-tasks-architecture-compliance.md` | Same; `validate-orchestrator.ps1:146`. |
| `.agents/30-context/architecture/module-anatomy.md` | A 13-line redirect with no content; the pointer is already in `architecture/readme.md:54`. Remove its line from `context-index.md` in the same change, or `validate-agent-contracts.py` fails. |

Not a removal, a correction to the first pass: `nkd-tdd-assessment` is not dead. `nkda-tddsn-assessment` lines 24 and 41 require it as an input, and it holds the full 12-dimension method that the tddsn skill only summarises. It is shared, merged into the tddsn assessment as its reference.

Defects to fix while moving (not removals): `manifest.md:29–32` names four command files that do not exist; `.specify/extensions/.registry` lists `ralph`; the backlog-item template and `nkda-backlog-item-prestart` cite "§0.5" where the rule is §2.5; the session skills name Specification, Implementation, Reviewer and Orchestrator agents that no file defines.

---

## 5. Open decisions

1. **Keep the existing labels, or rename to `state:*`?** Recommendation: keep them; make the plugin fit (P1). 38 open items already carry them, and other adopters will have labels too.

2. **Where does evidence live?** Recommendation: the assessment in an issue comment and the completion report in the pull request, as the guardrail already says; the description stays the unedited statement of what is expected (P4).

3. **A subagent inside a workflow run, or one worker chat per item?** Recommendation: one worker chat per item. ADR-0034 ratified the manager model, and a running workflow cannot be checked in on.

4. **Re-scope the standing authorisation before the backlog skills and workflows move.** Recommendation: amend ADR-0034 and `consent-policy.yaml` to name the plugin's runner, stage-gate tool and the two moved skills acting on issues labelled `backlog-item`, with the same permits and exclusions. Until then both workflows stay here.

5. **Plugin namespaces change command names.** A skill in a plugin is called `nkda-engineering-practice:<name>`. `.specify/extensions.yml` hooks, `validate-orchestrator.ps1` and `strict-implement` all call the bare names. Recommendation: move the Spec Kit-hooked skills last (`connector-coverage-check`, `nkda-observability-contract`, the three compliance skills, `nkda-refactor`, test-validity, test-promotion, `update-docs`, `nkda-core-definition-of-done`), after `Test-Orchestrator` can resolve a plugin skill.

6. **Is the route-first harness shared, and where?** It is now read in full: the six schemas are structural and carry no product vocabulary; the loader order, the contract validator, `configure.ps1`, the two READMEs that say how to write context files and stubs, and the `contract-schemas` CI job are method. The catalog contents, guardrail texts, context files and stubs are this repository's data. Recommendation: share it as a `harness` part of the practice plugin, after the skills, because every split skill already reads rule files through the practice definition and does not need the harness to work. It fixes one convention on adopters: everything lives under `.agents/`.

13. **The generic guardrails become plugin rule text; who owns them afterwards?** 17 guardrails split or move. Once the general rules live in the plugin, this repository's copies shrink to product rules plus a pointer. Recommendation: move `architecture-perspectives-ethos.md` first (no product text, and the archcheck skills depend on it), merge the duplicated rules before moving the rest, and keep each repository free to add stricter rules in its own files; the plugin's text is the floor.

14. **Three statements of one procedure, twice over.** Failing tests: `docs/failing-tests-workflow.md`, its guardrail, and `nkda-debugtests`. Tests-first session: `docs/test-first-workflow.md`, its guardrail, and the session skills. Recommendation: in the plugin, one skill with the doc as its reference file; here, the guardrail and the doc shrink to this repository's commands and paths plus a pointer.

15. **Which session model is right?** `docs/test-first-workflow.md` gives each phase to an existing skill or command and logs to `.md`; the session skills name four agents that do not exist and write `.json`; CI accepts only `.json`. Recommendation: the doc's owners with the `.json` evidence file. That replaces the recommendation in decision 8 to write four new agents: rewrite `start-session`, `review`, `fix` and `end-session` to the doc's phase owners instead.

7. **Provenance of `nkda-archimprove-codebase`.** Recommendation: confirm the source and its licence. If it is upstream, list it as a dependency and keep only the two local link lines here; `nkda-archcheck-architecture-review` and the archcheck workflow then call it by its upstream name.

8. **The tests-first session skills name agents that do not exist.** Recommendation: write the four agent definitions (Specification, Implementation, Reviewer, Orchestrator) as part of the practice plugin before sharing `session-start`, `review`, `fix` and `end`; without them the kit cannot run for anyone else. Remove `session-hooks`.

9. **One practice definition file, or one per concern?** Recommendation: one `definition-of-practice.json` whose sections point at files the repository already has (P15). It matches the delivery plugin's single definition and keeps the rule text where it is.

10. **`nkda-testcategory-workflow.js` commits per file.** Recommendation: make committing opt-in when it moves; as written it breaks Constitution rule 11 here and would surprise any other user.

11. **Create the missing labels now?** Recommendation: yes, independently of everything else: `in-progress`, `class-c-approved`, `evidence-waived`.

12. **Order of work.** Recommendation:
    1. Labels; write this repository's `definition-of-workflow.md` + `.json`.
    2. Delivery plugin P1–P4, P7, P11, agreed with the Hub and migration-tools CONTROL sessions.
    3. Practice plugin scaffold (P14, P15) with the 25 share-as-is items and `definition-of-practice.json` for this repository.
    4. The archcheck and test-quality splits, which share the `architecture` and `tests` sections.
    5. Delivery P5, P6, P8–P10, P12, P13; then decision 4; then retire the two backlog workflows here.
    6. The Spec Kit-hooked skills (decision 5), then the harness (decision 6).
