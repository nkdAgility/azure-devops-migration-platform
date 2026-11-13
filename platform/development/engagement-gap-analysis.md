# Engagement gap analysis: what keeps real engagements on Migration Tools

Date: 2026-11-13. Status: draft for operator review. Nothing here is a created issue.

This analysis compares the azure-devops-migration-tools (ADMT) work done on recent real
engagements with ADMP as it is today. It ranks the gaps by how much ADMT use each one would
remove. Engagements are anonymised as letters and described only by size and type. The mapping
from letters to engagements is held outside this repository.

## 1. Engagements

| Id | Type | Source → target | Size | Tooling used |
| --- | --- | --- | --- | --- |
| A | Multi-project carve-out (8 sub-migrations) | Azure DevOps Services → Azure DevOps Services; one sub-migration → Azure DevOps Server (on-prem) | Largest sub-migration: about 11.2k work items, about 149k source revisions, 144 repos (36.6 GB, LFS), 72 build definitions, 22 test plans / 234 suites / 583 test cases, a NuGet feed (98 packages, 4,331 versions). Others: 0.8k–3.6k work items, 5k–46k revisions, 9–115 repos. | ADMT 16.3.x for work items, test plans, pipelines, queries; in-house scripts for repos, feeds, wiki/comment link fix-ups |
| B | Selective programme move under governance | Azure DevOps Services → Azure DevOps Services (same tenant) | Pilot scale: 2 repos (6.6 GB / 120k commits; 0.3 GB), 1 bug with 12 revisions, 6 test cases, 1 test plan with 7 suites; 305 repos catalogued | ADMT 16.3.4-preview for work items and test plans; in-house scripts for repos |
| C | Two-project org-to-org move | Azure DevOps Services → Azure DevOps Services; one project nested under an area of another | Volumes not recorded | ADMT 16.1 for work items, test artefacts, pipelines, queries; process-migrator for process; script for repos |
| D | Whole-collection move | Azure DevOps Server (on-prem) → Azure DevOps Services | 12 projects, custom process | Microsoft Data Import Tool, not ADMT. Out of scope for ADMP parity; listed so its absence is explicit. |

Two further workspaces were examined and are not migrations (a flow-analytics engagement and the
marketing site). They are not counted.

**Finding that shapes the ranking.** Every ADMT engagement (A, B, C) was Azure DevOps Services as
source. None used a TFS source. The highest-value work is therefore on the net10
`MigrationAgent` (ADO export and import). The net481 `TfsMigrationAgent` matters only for A's
one on-prem target, and that target was reached over REST.

## 2. What ADMT was used for, per engagement

### Engagement A

- **Processors:** `TfsWorkItemMigrationProcessor` (WIQL excluding Test Plan/Suite, Shared Steps,
  Shared Parameter, Feedback types; `FixHtmlAttachmentLinks`; `WorkItemCreateRetryLimit: 5`;
  `GenerateMigrationComment`; `FilterWorkItemsThatAlreadyExistInTarget: true` for restarts),
  `TfsTestPlansAndSuitesMigrationProcessor`, `TfsSharedQueryProcessor`,
  `AzureDevOpsPipelineProcessor` (build, release, task groups, variable groups, service
  connections), `TfsTeamSettingsProcessor`/`Tool`. (A: sub-migration 1, 5 and 7 configs; A sessions.)
- **Tools:** `TfsNodeStructureTool` (regex area/iteration mapping into an archive node,
  `ReplicateAllExistingNodes`, `ShouldCreateMissingRevisionPaths`), `FieldMappingTool` incl.
  `TreeToTagFieldMap` (`toSkip: 1`), `WorkItemTypeMappingTool` (User Story → Product Backlog Item,
  Issue → Impediment), `TfsWorkItemTypeValidatorTool`, `TfsUserMappingTool`. (A: sub-migration 3, 5.)
- **Batching:** the largest work-item run was split into four "bites" with separate configs:
  closed items, the rest, test cases, and "changed since bite 3". (A: sub-migration 5 configs.)
- **Workarounds:** repo, feed and pipeline-artifact copies by in-house scripts; commit-mention
  linking switched off around pushes via an undocumented endpoint; repair passes after the
  migration (Closed Date on 5,304 items, people on about 3,700, 3,913 commit links repointed);
  a target-process alignment script to create missing custom fields; destroy-and-retry scripts
  for 44 partial work items on the on-prem target; 116 branch policies recreated by hand;
  service-connection secrets re-entered; shared queries split into their own run after failing in
  the structure run. (A: sub-migrations 3, 4, 5, 7 notes.)
- **What broke:** VS403417 "ChangedBy cannot be empty when BypassRules" (fatal); missing
  iterations blocking progress; `IndexOutOfRangeException` in `TfsGitRepositoryInfo.CreateFromGit`;
  TF26198 on linked items; `ReflectedWorkItemId` missing on Shared Parameter; team settings step
  crashed and was turned off; type-change replay needing a target PAT even in Prompt mode
  (TF400813), leaving partial items that a re-run skipped; per-type `ExcludeSourceFields`
  replacing rather than merging the `*` list; empty `ApplyTo` throwing; `TreeToTagFieldMap` docs
  wrong; repo/artifact scripts hard-coding the cloud host; 1,875 commit links still pointing at
  the source after the run; 30 NuGet versions missing; wiki not migrated; identities written as
  plain strings when users did not yet exist in the target. (A: sub-migration 3, 5 logs/notes; A sessions.)

### Engagement B

- **Processors:** `TfsWorkItemMigrationProcessor` (WIQL scope),
  `TfsTestVariablesMigrationProcessor` → `TfsTestConfigurationsMigrationProcessor` →
  `TfsTestPlansAndSuitesMigrationProcessor` (`OnlyElementsWithTag`, `TestPlanQuery`).
- **Tools:** `TfsNodeStructureTool` (regex area/iteration remap to a deeper target hierarchy,
  `ReplicateAllExistingNodes: false`), `FieldMappingTool` (operator rule: never off),
  `TfsTeamSettingsTool` disabled (teams are owned by governance).
- **Workarounds:** repo renames on push; commit-mention linking off/on with read-back checks;
  target auth switched to interactive Entra after a PAT 401; requirement work items added by hand
  so requirement-based suites could build; test variables/configurations processors added and the
  plan re-run; 2 teams created in error deleted by hand. (B: migration 02 and 03 notes.)
- **What broke:** an incident where commit-mention linking during a repo push created 12,018
  links on 6,793 work items org-wide and changed 2 items' state; the existing-item filter rewrote
  the WIQL by plain text replacement of the project name, giving TF51011; TF30063 401 setting a
  suite state; VS402485 from team settings on a missing area; requirement-based suites skipped
  ("Target work item not found"); test configurations not found because never migrated; 1–3
  minutes per suite lookup; plans and suites get no tags or links; existing target items receive
  only the reflected ID and field maps, not the source's current state; personal queries not
  migrated. (B: logs of 2026-11-10; B sessions.)

### Engagement C

- **Processors:** `AzureDevOpsPipelineProcessor` (all Migrate* flags on), five
  `TfsWorkItemMigrationProcessor` runs split by type (all non-test types → Shared Steps/Shared
  Parameter → Test Case → test variables and configurations → plans and suites),
  `TfsSharedQueryProcessor`.
- **Tools:** `TfsNodeStructureTool` (regex nesting of one project under another project's area),
  `FieldMappingTool` (FieldToFieldMap preserving the old reflected ID in a spare field),
  `WorkItemTypeMappingTool` (a type name containing a non-Latin look-alike letter mapped to its
  Latin spelling), `TfsTeamSettingsTool` disabled.
- **Workarounds:** process copied with microsoft/process-migrator; repos by a mirror-clone script.
- **What broke:** a token committed in a script had to be revoked. No run failures recorded.

## 3. ADMP today (summary)

From `README.md` (Feature parity, Path to parity), `docs/module-development-guide.md` (module
tables) and the open `backlog-item` issues:

- Done: work item and revision **export**; field transforms (14, incl. `TreeToTagTransform`);
  area/iteration node mapping; identity mapping; teams for ADO; WIQL and regex scoping.
- Import of work items, attachments, embedded images, ID-map sync, batching cursor: 🔜
  (#150–#154).
- Planned: commit/changeset link remap (#155), link fidelity (#156), TFS board settings (#157),
  shared queries (#160), test plans (#161), process definitions (#162), pipelines (#163), bulk
  utilities (#164), TFS target beyond work items (#172).
- Not found in `src/` or issues: work item **type** mapping and type-change replay, incremental
  re-sync of changed items, git repository migration (GitModule is "planned" in the module guide
  with no Backlog Item), package feed migration.

## 4. Ranked gaps

Rank = how much ADMT use the gap removes across A, B, C (engagement count × share of the
ADMT work in that engagement). Agent column: **net10** = `MigrationAgent`; **net481** =
`TfsMigrationAgent`.

| Rank | Gap | Needed by | Covered by | ADMT mistakes to avoid | Agent |
| --- | --- | --- | --- | --- | --- |
| 1 | Production-ready ADO work item import (revisions, attachments, embedded images, ID map, batching) | A, B, C — every run | #150, #151, #152, #153, #154 | VS403417 ChangedBy empty under BypassRules; partial items skipped on re-run (A); ReflectedWorkItemId field missing on a type is fatal (B log; ADMT #2578, #3248); `FilterWorkItemsThatAlreadyExistInTarget` rewrites WIQL by text (B); TF26194 on fix-up save (ADMT #3240) | net10 |
| 2 | Test plans, suites, configurations, variables, shared steps/parameters | A (22 plans), B (the whole migration), C | #161 | Requirement-based suites skipped when the requirement is absent (B); configurations/variables must precede plans (B, C); slow per-suite lookup (B); plans/suites lose tags and links (B); shared steps/parameters can't hold ReflectedWorkItemId on inherited targets (ADMT #3248, #2578); fails on invalid suite queries (ADMT #3021) | net10 |
| 3 | Work item type mapping, incl. type-change history | A, C | **None — draft N1** | Type-changed item created as its first-revision type and fails with misleading VS403417 (ADMT #3168); renamed types need both names in `ApplyTo`; empty `ApplyTo` throws (A) | net10 (shared Infrastructure.Agent tool) |
| 4 | Pipelines: build/release, task groups, variable groups, service connections | A (72–106 build defs per sub-migration), C | #163 | Task group null reference crashes the run (ADMT #2976); exit code 0 after fatal error (ADMT #3170); secrets need re-entry and branch policies by hand (A) | net10 |
| 5 | Git commit / PR link remap | A (3,913 repointed by script, 1,875 still wrong) | #155 | PR links dropped (ADMT #3215); `CreateFromGit` throws on malformed links (ADMT #3188, #3069; A) | net10 |
| 6 | Shared queries (and personal queries on request) | A, C; B asked for personal queries | #160 | Query run failed inside the structure run and had to be isolated (A); queries must be rewritten for remapped area paths | net10 |
| 7 | Incremental re-sync: re-apply source state to items already in the target | A ("changed since" bite), B (existing items get only reflected ID + field maps) | **None — draft N2** | Text-replace WIQL rewrite (B); TF26198 on re-sync of already-remapped mentions (ADMT #3212) | net10 |
| 8 | Git repository migration with commit-mention linking held off | A (144+ repos), B (2 large, 305 catalogued), C | **None — draft N3** (GitModule planned, no item) | The org-wide link incident (B: 12,018 links); hard-coded cloud host blocks on-prem target (A); oversize/LFS handling (A) | net10 |
| 9 | Team settings that don't crash or create stray teams | A (crashed, turned off), B (stray teams, VS402485) | Teams ✅ ADO; #157 (TFS) | Crash in `MigrateTeamSettings` (A, B); teams created when disabled-by-intent (B); missing area path fatal (B) | net10 |
| 10 | Process/target-schema alignment before import | A (alignment script, missing custom fields), C (process-migrator) | #162 (readiness check exists) | Picklist created as text (ADMT #3241); hidden field not hidden (ADMT #3242) | net10 |
| 11 | Azure DevOps Server as an import target | A (one sub-migration) | #172 partly; verify ADO connector against Server | PAT guard checked only cloud hosts; REST call needed a PAT in Prompt mode (A) | net10 via REST (net481 only if Server version needs the Object Model) |
| — | Package feeds and wiki link fix-ups | A | **None** — beyond ADMT; not drafted | Not an ADMT feature, so it removes script use, not ADMT use | net10 |

Gaps 1, 2, 4, 5 and 6 are already on the backlog and on the README's Path to parity. The
engagements confirm that order with one change: **test plans (#161) should come straight after
work item import**, ahead of commit-link remap and shared queries, because B's whole migration
was a test plan and A had 22.

## 5. Draft Backlog Items for uncovered gaps

### N1. Work item type mapping with type-change history

## Outcome

An operator can map source work item types to different target types (e.g. User Story → Product
Backlog Item) and migrate items whose type changed during their history, so each target item
ends with the mapped final type and the full revision history.

## Why

Engagements A and C both needed ADMT `WorkItemTypeMappingTool` (A: two type maps; C: a type name
with a look-alike non-Latin letter). No type-mapping option or transform exists in `src/`
(grep for `WorkItemTypeMap` finds nothing). ADMT #3168: a type-changed item is created as its
first-revision type and fails with VS403417 when that type is disabled in the target.

## Acceptance criteria

- [ ] A `WorkItemTypeMappings` config maps source type names to target type names; Prepare reports unmapped source types that don't exist in the target.
- [ ] An item whose type changed in its history is created and replayed so its target type at each revision follows the map, and its final type matches the mapped final source type.
- [ ] A map that names a missing target type fails in Prepare with an actionable message naming the type, not at import.
- [ ] Field transforms with an `ApplyTo` list match on both the source and the mapped type name; an empty `ApplyTo` is a validation error, not an exception.
- [ ] Re-running the import produces no duplicates and no type flip-flops (live E2E on ADO).

## Scope

- Connectors: Simulated, AzureDevOpsServices, TeamFoundationServer (export of type-change revisions)
- Phases: Prepare, Import, Validate
- Agents: shared multi-target code (Infrastructure.Agent tool); import on MigrationAgent
- Out of scope: changing the target process (see #162)

## azure-devops-migration-tools equivalent

| Behaviour in Migration Tools | Match / improve / drop (why) |
| --- | --- |
| `WorkItemTypeMappingTool` source → target type map | Match |
| Type-change replay over REST needing a target PAT even in Prompt mode | Improve: use the endpoint's own credential |
| `SkipToFinalRevisedWorkItemType` | Improve: per-revision mapping makes it unnecessary (ADMT #1756) |

## Links

- Depends on: #150
- Decisions:
- Spec:

### N2. Incremental re-sync of changed work items

## Outcome

An operator can re-run a work item migration after a cut-off and have every source item changed
since the last run brought up to date in the target (new revisions, fields, links,
attachments), without re-creating items or touching unchanged ones.

## Why

Engagement A ran a separate "changed since" batch to catch up before cutover. In engagement B,
items already in the target received only the reflected ID and field maps, not the source's
current state, and the ADMT existing-item filter rewrote the WIQL by plain text replacement
(TF51011). No incremental or delta feature exists in ADMP (no match in `src/`); checkpoint resume
only skips completed work.

## Acceptance criteria

- [ ] Export with an incremental flag writes only revisions newer than the package's last exported revision per item, appended to the existing package.
- [ ] Import applies only the new revisions to items already in the target, found through the ID map, never by rewriting the source query text.
- [ ] New links and attachments on changed items are added; existing ones are not duplicated.
- [ ] Validate reports per item that the target's latest revision matches the source's latest revision.
- [ ] A live E2E on ADO: migrate, change 3 source items, re-run, target matches with no duplicates.

## Scope

- Connectors: Simulated, AzureDevOpsServices, TeamFoundationServer
- Phases: Export, Import, Validate
- Agents: TfsMigrationAgent (TFS export), MigrationAgent (ADO export and import)
- Out of scope: two-way sync; changes made directly in the target

## azure-devops-migration-tools equivalent

| Behaviour in Migration Tools | Match / improve / drop (why) |
| --- | --- |
| Re-run `TfsWorkItemMigrationProcessor` with `FilterWorkItemsThatAlreadyExistInTarget` | Improve: package-cursor delta instead of a WIQL text rewrite |
| Existing target items updated with reflected ID and field maps only | Improve: replay the missing revisions |

## Links

- Depends on: #150, #151, #152
- Decisions: whether the delta cursor is per item or per package
- Spec:

### N3. Git repository migration with work item mention linking held off

## Outcome

An operator can migrate Git repositories (all refs, LFS, optional rename) from one Azure DevOps
project to another through the package, without the target creating commit-mention links on
work items during the push.

## Why

All three ADMT engagements moved repos with in-house scripts outside ADMT (A: 144 repos, 36.6 GB
with LFS; B: 6.6 GB / 120k commits; C: mirror script). In B, mention linking left on during a push
created 12,018 links on 6,793 work items across the target org and changed 2 items' state. A's
scripts hard-coded the cloud host and blocked an on-prem target. `docs/module-development-guide.md`
lists GitModule as planned; there is no Backlog Item.

## Acceptance criteria

- [ ] Export writes each selected repository to the package as a mirror bundle including LFS objects; ref and commit counts are recorded.
- [ ] Import creates the target repository (optionally under a mapped name) and pushes all refs; Validate compares ref and commit counts with the package.
- [ ] Commit-mention linking on the target is turned off before the push and restored afterwards, each change read back; a mismatch stops the job.
- [ ] Re-running skips repositories already complete and resumes a partly pushed one.
- [ ] Works against an on-prem Azure DevOps Server target URL as well as Services (live E2E on ADO).

## Scope

- Connectors: Simulated, AzureDevOpsServices, TeamFoundationServer (export of Git repos on the server)
- Phases: Inventory, Export, Import, Validate
- Agents: MigrationAgent
- Out of scope: TFVC history; branch policies; GitHub as a target

## azure-devops-migration-tools equivalent

| Behaviour in Migration Tools | Match / improve / drop (why) |
| --- | --- |
| None (ADMT does not migrate repos; it only remaps commit links via `TfsGitRepositoryTool`) | Improve: brings the repo step into the package so #155 can remap against known repos |

## Links

- Depends on:
- Decisions: whether the mention-linking toggle may use an undocumented endpoint
- Spec:

## 6. Sources

- Client workspaces A, B, C, D: migration configs, notes, logs and catalogues (cited by engagement
  letter only).
- Client-related Claude sessions for A and B (search snippets only; cited by engagement letter).
- ADMP: `README.md` (Feature parity, Path to parity), `docs/module-development-guide.md`,
  `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs`,
  `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsMigrationAgentServiceExtensions.cs`,
  `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/Transforms/`, open
  issues labelled `backlog-item`.
- ADMT issues: #1756, #2578, #2976, #3021, #3069, #3168, #3170, #3188, #3212, #3215, #3240, #3241,
  #3242, #3248.
