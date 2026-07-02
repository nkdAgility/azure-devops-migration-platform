export const meta = {
  name: 'nkda-archcheck-workflow',
  description: 'Architecture review, triage, and auto-fix. Pass args "report" to check+report only, "execute" to apply an existing report, or omit to do both.',
  phases: [
    { title: 'Modular Monolith',      detail: 'Execute skill: .agents/skills/nkda-archcheck-modular-monolith/SKILL.md' },
    { title: 'Clean Architecture',    detail: 'Execute skill: .agents/skills/nkda-archcheck-clean-architecture/SKILL.md' },
    { title: 'Hexagonal Architecture',detail: 'Execute skill: .agents/skills/nkda-archcheck-hexagonal/SKILL.md' },
    { title: 'Vertical Slice',        detail: 'Execute skill: .agents/skills/nkda-archcheck-vertical-slice/SKILL.md' },
    { title: 'Screaming Architecture',detail: 'Execute skill: .agents/skills/nkda-archcheck-screaming-architecture/SKILL.md' },
    { title: 'Architecture Deepening',detail: 'Execute skill: .agents/skills/nkda-archimprove-codebase/SKILL.md' },
    { title: 'Module Compliance',     detail: 'Compliance: .agents/30-context/domains/module-model.md + module-anatomy-contract.md' },
    { title: 'Orchestrator Compliance',detail: 'Compliance: .agents/30-context/domains/orchestrator-model.md + orchestrator-contract.md' },
    { title: 'Extensions Compliance', detail: 'Compliance: .agents/30-context/domains/connector-model.md + capability-seam-contract.md' },
    { title: 'Tools Compliance',      detail: 'Compliance: .agents/30-context/domains/module-model.md (Tools) + field-transform-contract.md' },
    { title: 'Triage',                detail: 'Classify every finding as Class A/B (auto-fix) or Class C (needs operator)' },
    { title: 'Report',                detail: 'Write analysis/archcheck/report.md and analysis/archcheck/triage.json' },
    { title: 'Auto-Fix',              detail: 'Apply Class A/B fixes in batches of 5; each batch is build + unit-test verified (and reverted if red) before the next (execute mode only)' },
    { title: 'Verify',                detail: 'Final full-suite gate: dotnet build + dotnet test; revert and escalate any breaking fix (execute mode only)' },
    { title: 'Commit',                detail: 'Never commits — verified fixes are left uncommitted for operator review (execute mode only)' },
    { title: 'Final Report',          detail: 'Update report with fix outcomes and operator action checklist (execute mode only)' },
  ],
}

// ---------------------------------------------------------------------------
// Mode selection
//   args = 'report'  — run checks, triage, write report + triage.json; stop
//   args = 'execute' — read existing triage.json, apply fixes in verified batches, update report (never commits)
//   args = anything else (or omitted) — run everything
// ---------------------------------------------------------------------------
const mode = typeof args === 'string' ? args.trim().toLowerCase() : 'both'
const doReport  = mode === 'report'  || mode === 'both'
const doExecute = mode === 'execute' || mode === 'both'

log(`Mode: ${mode === 'report' ? 'report only' : mode === 'execute' ? 'execute only' : 'report + execute'}`)

// ---------------------------------------------------------------------------
// Shared output schema — used by all ten check agents
// ---------------------------------------------------------------------------

const FINDINGS_SCHEMA = {
  type: 'object',
  properties: {
    perspective:        { type: 'string' },
    tag:                { type: 'string' },
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          id:       { type: 'string' },
          severity: { type: 'string', enum: ['Critical', 'High', 'Medium', 'Low', 'Informational'] },
          title:    { type: 'string' },
          file:     { type: 'string' },
          line:     { type: 'string' },
          fix:      { type: 'string' },
        },
        required: ['id', 'severity', 'title', 'fix'],
      },
    },
    summary:            { type: 'string' },
    criticalCount:      { type: 'number' },
    highCount:          { type: 'number' },
    mediumCount:        { type: 'number' },
    lowCount:           { type: 'number' },
    informationalCount: { type: 'number' },
  },
  required: ['perspective', 'tag', 'findings', 'summary', 'criticalCount', 'highCount', 'mediumCount', 'lowCount', 'informationalCount'],
}

// ---------------------------------------------------------------------------
// REPORT MODE — run all checks, triage, write report + triage.json
// ---------------------------------------------------------------------------

let autoFixable  = []
let needsOperator = []
let deepeningOnly = []
let allFindings   = []

let mmResult, caResult, hxResult, vsResult, saResult, dcResult
let mcResult, ocResult, ecResult, tcResult

if (doReport) {

  // ── Step 1 — Modular Monolith ──────────────────────────────────────────
  phase('Modular Monolith')
  mmResult = await agent(
    `Read and execute the skill exactly as documented in:
  .agents/skills/nkda-archcheck-modular-monolith/SKILL.md

Scope: entire solution (src/ directory).
Tag all findings [MM]. Use IDs: MM-C<n>, MM-H<n>, MM-M<n>, MM-L<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'check:modular-monolith', phase: 'Modular Monolith', schema: FINDINGS_SCHEMA }
  )
  log(`Modular Monolith: ${mmResult?.criticalCount ?? 0} Critical, ${mmResult?.highCount ?? 0} High, ${mmResult?.mediumCount ?? 0} Medium, ${mmResult?.lowCount ?? 0} Low`)

  // ── Step 2 — Clean Architecture ───────────────────────────────────────
  phase('Clean Architecture')
  caResult = await agent(
    `Read and execute the skill exactly as documented in:
  .agents/skills/nkda-archcheck-clean-architecture/SKILL.md

Scope: entire solution (src/ directory).
Tag all findings [CA]. Use IDs: CA-C<n>, CA-H<n>, CA-M<n>, CA-L<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'check:clean-architecture', phase: 'Clean Architecture', schema: FINDINGS_SCHEMA }
  )
  log(`Clean Architecture: ${caResult?.criticalCount ?? 0} Critical, ${caResult?.highCount ?? 0} High, ${caResult?.mediumCount ?? 0} Medium, ${caResult?.lowCount ?? 0} Low`)

  // ── Step 3 — Hexagonal ────────────────────────────────────────────────
  phase('Hexagonal Architecture')
  hxResult = await agent(
    `Read and execute the skill exactly as documented in:
  .agents/skills/nkda-archcheck-hexagonal/SKILL.md

Scope: entire solution (src/ directory).
Tag all findings [HX]. Use IDs: HX-C<n>, HX-H<n>, HX-M<n>, HX-L<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'check:hexagonal', phase: 'Hexagonal Architecture', schema: FINDINGS_SCHEMA }
  )
  log(`Hexagonal: ${hxResult?.criticalCount ?? 0} Critical, ${hxResult?.highCount ?? 0} High, ${hxResult?.mediumCount ?? 0} Medium, ${hxResult?.lowCount ?? 0} Low`)

  // ── Step 4 — Vertical Slice ───────────────────────────────────────────
  phase('Vertical Slice')
  vsResult = await agent(
    `Read and execute the skill exactly as documented in:
  .agents/skills/nkda-archcheck-vertical-slice/SKILL.md

Scope: entire solution (src/ and features/ directories).
Tag all findings [VS]. Use IDs: VS-C<n>, VS-H<n>, VS-M<n>, VS-L<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'check:vertical-slice', phase: 'Vertical Slice', schema: FINDINGS_SCHEMA }
  )
  log(`Vertical Slice: ${vsResult?.criticalCount ?? 0} Critical, ${vsResult?.highCount ?? 0} High, ${vsResult?.mediumCount ?? 0} Medium, ${vsResult?.lowCount ?? 0} Low`)

  // ── Step 5 — Screaming Architecture ──────────────────────────────────
  phase('Screaming Architecture')
  saResult = await agent(
    `Read and execute the skill exactly as documented in:
  .agents/skills/nkda-archcheck-screaming-architecture/SKILL.md

Scope: entire solution (src/ and features/ directories).
Tag all findings [SA]. Use IDs: SA-H<n>, SA-M<n>, SA-L<n>, SA-I<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'check:screaming-architecture', phase: 'Screaming Architecture', schema: FINDINGS_SCHEMA }
  )
  log(`Screaming Architecture: ${saResult?.highCount ?? 0} High, ${saResult?.mediumCount ?? 0} Medium, ${saResult?.lowCount ?? 0} Low, ${saResult?.informationalCount ?? 0} Informational`)

  // ── Step 6 — Architecture Deepening ──────────────────────────────────
  phase('Architecture Deepening')
  dcResult = await agent(
    `Read and execute the skill exactly as documented in:
  .agents/skills/nkda-archimprove-codebase/SKILL.md

Also read all supporting files in that skill folder:
  .agents/skills/nkda-archimprove-codebase/DEEPENING.md
  .agents/skills/nkda-archimprove-codebase/INTERFACE-DESIGN.md
  .agents/skills/nkda-archimprove-codebase/LANGUAGE.md

Scope: entire solution (src/ directory).
Tag all findings [DC]. Use IDs: DC-H<n>, DC-M<n>, DC-L<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'check:architecture-deepening', phase: 'Architecture Deepening', schema: FINDINGS_SCHEMA }
  )
  log(`Architecture Deepening: ${dcResult?.highCount ?? 0} High, ${dcResult?.mediumCount ?? 0} Medium, ${dcResult?.lowCount ?? 0} Low`)

  // ── Step 7 — Module Compliance ────────────────────────────────────────
  phase('Module Compliance')
  mcResult = await agent(
    `Audit every IModule implementation in the codebase for compliance with the documented Module Model.

Read and apply all rules from these authoritative sources — do not rely on prior knowledge:
  .agents/30-context/domains/module-model.md
  .agents/10-contracts/specs/module-anatomy-contract.md

Search scope: src/ directory.
Find all classes implementing IModule. For each, verify compliance with every rule in the above documents.
For each violation, state which rule (quoted from the document) is violated.

Tag all findings [MC]. Use IDs: MC-C<n>, MC-H<n>, MC-M<n>, MC-L<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'compliance:module', phase: 'Module Compliance', schema: FINDINGS_SCHEMA }
  )
  log(`Module Compliance: ${mcResult?.criticalCount ?? 0} Critical, ${mcResult?.highCount ?? 0} High, ${mcResult?.mediumCount ?? 0} Medium, ${mcResult?.lowCount ?? 0} Low`)

  // ── Step 8 — Orchestrator Compliance ─────────────────────────────────
  phase('Orchestrator Compliance')
  ocResult = await agent(
    `Audit every I*Orchestrator implementation in the codebase for compliance with the documented Orchestrator Model.

Read and apply all rules from these authoritative sources — do not rely on prior knowledge:
  .agents/30-context/domains/orchestrator-model.md
  .agents/10-contracts/specs/orchestrator-contract.md

Search scope: src/ directory.
Find all classes implementing an I*Orchestrator interface. For each, verify compliance with every rule in the above documents.
For each violation, state which rule (quoted from the document) is violated.

Tag all findings [OC]. Use IDs: OC-C<n>, OC-H<n>, OC-M<n>, OC-L<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'compliance:orchestrator', phase: 'Orchestrator Compliance', schema: FINDINGS_SCHEMA }
  )
  log(`Orchestrator Compliance: ${ocResult?.criticalCount ?? 0} Critical, ${ocResult?.highCount ?? 0} High, ${ocResult?.mediumCount ?? 0} Medium, ${ocResult?.lowCount ?? 0} Low`)

  // ── Step 9 — Extensions / Adapters Compliance ─────────────────────────
  phase('Extensions Compliance')
  ecResult = await agent(
    `Audit every IModuleExtension and *Adapter implementation in the codebase for compliance with the documented Extension-Adapter model.

Read and apply all rules from these authoritative sources — do not rely on prior knowledge:
  .agents/30-context/domains/connector-model.md
  .agents/30-context/domains/capability-seam-contract.md
  .agents/10-contracts/specs/module-anatomy-contract.md

Search scope: src/ directory.
Find all IModuleExtension implementations and all *Adapter classes. For each, verify compliance with every rule in the above documents.
For each violation, state which rule (quoted from the document) is violated.

Tag all findings [EC]. Use IDs: EC-C<n>, EC-H<n>, EC-M<n>, EC-L<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'compliance:extensions', phase: 'Extensions Compliance', schema: FINDINGS_SCHEMA }
  )
  log(`Extensions Compliance: ${ecResult?.criticalCount ?? 0} Critical, ${ecResult?.highCount ?? 0} High, ${ecResult?.mediumCount ?? 0} Medium, ${ecResult?.lowCount ?? 0} Low`)

  // ── Step 10 — Tools Compliance ────────────────────────────────────────
  phase('Tools Compliance')
  tcResult = await agent(
    `Audit every *Tool / I*Tool implementation in the codebase for compliance with the documented stateless-tool model.

Read and apply all rules from these authoritative sources — do not rely on prior knowledge:
  .agents/30-context/domains/module-model.md        (Tools section)
  .agents/30-context/domains/capability-seam-contract.md
  .agents/10-contracts/specs/field-transform-contract.md

Search scope: src/ directory.
Find all classes and interfaces matching the Tool naming pattern. For each, verify compliance with every rule in the above documents.
Also scan for copy-paste transformation logic across modules that the documents indicate should be a canonical Tool — flag these as Low/Informational deepening gaps.
For each violation, state which rule (quoted from the document) is violated.

Tag all findings [TC]. Use IDs: TC-C<n>, TC-H<n>, TC-M<n>, TC-L<n>, TC-I<n>.
Return a structured result with all findings and per-severity counts.`,
    { label: 'compliance:tools', phase: 'Tools Compliance', schema: FINDINGS_SCHEMA }
  )
  log(`Tools Compliance: ${tcResult?.criticalCount ?? 0} Critical, ${tcResult?.highCount ?? 0} High, ${tcResult?.mediumCount ?? 0} Medium, ${tcResult?.lowCount ?? 0} Low, ${tcResult?.informationalCount ?? 0} Informational`)

  // ── Step 11 — Triage ──────────────────────────────────────────────────
  phase('Triage')

  allFindings = [mmResult, caResult, hxResult, vsResult, saResult, dcResult, mcResult, ocResult, ecResult, tcResult]
    .filter(Boolean)
    .flatMap(r => (r.findings ?? []).map(f => ({ ...f, perspective: r.tag })))

  const TRIAGE_SCHEMA = {
    type: 'object',
    properties: {
      autoFixable: {
        type: 'array',
        items: {
          type: 'object',
          properties: {
            id:          { type: 'string' },
            title:       { type: 'string' },
            file:        { type: 'string' },
            line:        { type: 'string' },
            fix:         { type: 'string' },
            changeClass: { type: 'string', enum: ['A', 'B'] },
            rationale:   { type: 'string' },
          },
          required: ['id', 'title', 'fix', 'changeClass', 'rationale'],
        },
      },
      needsOperator: {
        type: 'array',
        items: {
          type: 'object',
          properties: {
            id:               { type: 'string' },
            title:            { type: 'string' },
            file:             { type: 'string' },
            line:             { type: 'string' },
            fix:              { type: 'string' },
            changeClass:      { type: 'string', enum: ['C'] },
            blockerReason:    { type: 'string' },
            requiredEvidence: { type: 'array', items: { type: 'string' } },
          },
          required: ['id', 'title', 'fix', 'changeClass', 'blockerReason', 'requiredEvidence'],
        },
      },
      deepeningOnly: {
        type: 'array',
        items: {
          type: 'object',
          properties: {
            id:    { type: 'string' },
            title: { type: 'string' },
            fix:   { type: 'string' },
          },
          required: ['id', 'title', 'fix'],
        },
      },
    },
    required: ['autoFixable', 'needsOperator', 'deepeningOnly'],
  }

  const triageResult = await agent(
    `Classify every finding below into three buckets using the governance rules.

Read these authoritative sources before classifying — apply their definitions exactly:
  .agents/10-contracts/change-classes.yaml
  .agents/10-contracts/consent-policy.yaml
  .agents/20-guardrails/core/change-governance.md
  .agents/10-contracts/specs/package-boundary-contract.md
  .agents/10-contracts/specs/orchestrator-contract.md

Buckets:
- autoFixable   — Class A or B per change-classes.yaml. No operator involvement needed.
- needsOperator — Class C per change-classes.yaml. BLOCKED. Quote the exact rule that makes it Class C. List required evidence.
- deepeningOnly — DC and TC-I findings only (candidate refactors, not violations).

All findings to classify:
${JSON.stringify(allFindings, null, 2)}

Return three lists: autoFixable, needsOperator, deepeningOnly.`,
    { label: 'triage:findings', phase: 'Triage', schema: TRIAGE_SCHEMA }
  )

  autoFixable   = triageResult?.autoFixable   ?? []
  needsOperator  = triageResult?.needsOperator  ?? []
  deepeningOnly  = triageResult?.deepeningOnly  ?? []

  log(`Triage: ${autoFixable.length} auto-fixable, ${needsOperator.length} need operator, ${deepeningOnly.length} deepening-only`)

  // ── Step 12 — Write report + triage.json ──────────────────────────────
  phase('Report')

  await agent(
    `Write the combined architecture review report to analysis/archcheck/report.md.

Read the canonical report format from:
  .agents/skills/nkda-archcheck-architecture-review/SKILL.md

Extend it with:
1. An "⚠️ OPERATOR ACTION REQUIRED" section placed BEFORE the summary table. List every needsOperator item with its Class C blocker reason, required evidence checklist, and the fix pending consent. If none, write "No Class C changes identified."
2. Two extra columns in the summary table: "Auto-fix" and "Needs Operator" counts per perspective.
3. A triage status badge on every violation entry: [AUTO-FIX QUEUED] or [NEEDS OPERATOR].
4. For compliance findings (MC, OC, EC, TC), quote the specific rule from the source document that is violated.

All ten perspectives must appear in the summary table: MM, CA, HX, VS, SA, DC, MC, OC, EC, TC.

Findings:
MM: ${JSON.stringify(mmResult?.findings ?? [])}
CA: ${JSON.stringify(caResult?.findings ?? [])}
HX: ${JSON.stringify(hxResult?.findings ?? [])}
VS: ${JSON.stringify(vsResult?.findings ?? [])}
SA: ${JSON.stringify(saResult?.findings ?? [])}
DC: ${JSON.stringify(dcResult?.findings ?? [])}
MC: ${JSON.stringify(mcResult?.findings ?? [])}
OC: ${JSON.stringify(ocResult?.findings ?? [])}
EC: ${JSON.stringify(ecResult?.findings ?? [])}
TC: ${JSON.stringify(tcResult?.findings ?? [])}

Triage:
autoFixable:   ${JSON.stringify(autoFixable)}
needsOperator: ${JSON.stringify(needsOperator)}
deepeningOnly: ${JSON.stringify(deepeningOnly)}

Also write analysis/archcheck/triage.json containing exactly:
{
  "generatedAt": "<ISO date>",
  "autoFixable": <autoFixable array>,
  "needsOperator": <needsOperator array>,
  "deepeningOnly": <deepeningOnly array>
}

This file is the machine-readable input for execute mode. Write it last, after the report.`,
    { label: 'report:initial', phase: 'Report' }
  )

  log(`Report written to analysis/archcheck/report.md`)
  log(`Triage written to analysis/archcheck/triage.json`)

  if (mode === 'report') {
    log(`Report mode complete. Run with args="execute" to apply fixes.`)
    return {
      mode: 'report',
      totalFindings: allFindings.length,
      autoFixableCount: autoFixable.length,
      needsOperatorCount: needsOperator.length,
      deepeningOnlyCount: deepeningOnly.length,
      reportPath: 'analysis/archcheck/report.md',
      triagePath: 'analysis/archcheck/triage.json',
    }
  }
}

// ---------------------------------------------------------------------------
// EXECUTE MODE — read triage.json, apply fixes, verify, commit, update report
// ---------------------------------------------------------------------------

if (doExecute) {

  // If we just ran report mode, autoFixable/needsOperator are already populated.
  // If we are in execute-only mode, read them from the saved triage.json.
  if (mode === 'execute') {
    phase('Triage')

    const LOAD_SCHEMA = {
      type: 'object',
      properties: {
        autoFixable:   { type: 'array', items: { type: 'object' } },
        needsOperator: { type: 'array', items: { type: 'object' } },
        deepeningOnly: { type: 'array', items: { type: 'object' } },
        generatedAt:   { type: 'string' },
      },
      required: ['autoFixable', 'needsOperator', 'deepeningOnly'],
    }

    const loaded = await agent(
      `Read analysis/archcheck/triage.json and return its contents.
If the file does not exist, return empty arrays and set generatedAt to "missing".`,
      { label: 'load:triage', phase: 'Triage', schema: LOAD_SCHEMA }
    )

    autoFixable   = loaded?.autoFixable   ?? []
    needsOperator  = loaded?.needsOperator  ?? []
    deepeningOnly  = loaded?.deepeningOnly  ?? []

    if (!loaded || loaded.generatedAt === 'missing') {
      log('⚠️  analysis/archcheck/triage.json not found. Run with args="report" first.')
      return { mode: 'execute', error: 'triage.json missing — run report mode first' }
    }

    log(`Loaded triage.json (generated: ${loaded.generatedAt}): ${autoFixable.length} auto-fixable, ${needsOperator.length} need operator`)
  }

  // ── Step 13 — Auto-fix ────────────────────────────────────────────────
  phase('Auto-Fix')

  const FIX_SCHEMA = {
    type: 'object',
    properties: {
      id:            { type: 'string' },
      status:        { type: 'string', enum: ['applied', 'skipped', 'failed'] },
      changeClass:   { type: 'string' },
      filesModified: { type: 'array', items: { type: 'string' } },
      summary:       { type: 'string' },
      skipReason:    { type: 'string' },
    },
    required: ['id', 'status', 'changeClass', 'filesModified', 'summary'],
  }

  const fixResults = []

  const BATCH_SIZE = 5

  const BATCH_VERIFY_SCHEMA = {
    type: 'object',
    properties: {
      buildStatus:  { type: 'string', enum: ['passed', 'failed'] },
      testStatus:   { type: 'string', enum: ['passed', 'failed'] },
      reverted:     { type: 'boolean' },
      summary:      { type: 'string' },
    },
    required: ['buildStatus', 'testStatus', 'reverted', 'summary'],
  }

  if (autoFixable.length === 0) {
    log('No auto-fixable items — nothing to apply.')
  } else {
    const batches = []
    for (let i = 0; i < autoFixable.length; i += BATCH_SIZE)
      batches.push(autoFixable.slice(i, i + BATCH_SIZE))

    log(`Applying ${autoFixable.length} auto-fixable items in ${batches.length} batch(es) of up to ${BATCH_SIZE}; each batch is build+test verified before the next begins…`)

    for (let b = 0; b < batches.length; b++) {
      const batch = batches[b]
      const batchResults = []
      log(`── Batch ${b + 1}/${batches.length}: ${batch.map(i => i.id).join(', ')}`)

      for (const item of batch) {
        log(`Fixing ${item.id}: ${item.title}`)

        const fixResult = await agent(
          `Apply this pre-triaged Class ${item.changeClass} architecture fix.

ID:          ${item.id}
Title:       ${item.title}
File:        ${item.file ?? 'see fix description'}
Line:        ${item.line ?? 'see fix description'}
Fix:         ${item.fix}
Class:       ${item.changeClass}
Rationale:   ${item.rationale}

Rules:
1. Read the affected file(s) before editing.
2. Apply the minimal safe fix exactly as described — do not refactor beyond it.
3. New files only if the fix explicitly requires one.
4. Renames: update usages within the same project only unless the fix says otherwise.
5. Class B: also update any doc/context file the fix identifies.
6. If mid-fix you discover the change would alter a canonical surface contract, STOP and return status=skipped with a clear skipReason.
7. Test-first for behavioural changes: if this fix changes runtime behaviour (not just docs, comments, names, or dead code), update or add the covering test in the SAME fix so the test asserts the new behaviour — list those test files in filesModified.
8. Do NOT run dotnet build or dotnet test yourself — the batch verifier does that after this batch.
9. Do NOT commit or stage anything.

Return: status (applied/skipped/failed), filesModified, summary.`,
          { label: `fix:${item.id}`, phase: 'Auto-Fix', schema: FIX_SCHEMA }
        )

        const record = { ...item, fixResult }
        fixResults.push(record)
        batchResults.push(record)

        const status = fixResult?.status ?? 'unknown'
        if (status === 'applied') {
          log(`✅ ${item.id} — applied (${(fixResult?.filesModified ?? []).join(', ') || 'no files listed'})`)
        } else if (status === 'skipped') {
          log(`⏭ ${item.id} — skipped: ${fixResult?.skipReason ?? 'no reason given'}`)
          needsOperator.push({
            ...item,
            changeClass:      'C',
            blockerReason:    `Escalated during auto-fix: ${fixResult?.skipReason ?? 'would alter canonical surface'}`,
            requiredEvidence: ['Explicit operator consent', 'ADR add/update', 'Contract compatibility tests'],
          })
        } else {
          log(`❌ ${item.id} — failed: ${fixResult?.summary ?? 'unknown error'}`)
        }
      }

      const batchApplied = batchResults.filter(r => r.fixResult?.status === 'applied')
      if (batchApplied.length === 0) {
        log(`Batch ${b + 1}: nothing applied — skipping verification.`)
        continue
      }

      const batchVerify = await agent(
        `Verify the current batch of architecture fixes builds and passes tests. Fixes in this batch:
${batchApplied.map(r => `  [${r.id}] ${r.title} — files: ${(r.fixResult?.filesModified ?? []).join(', ')}`).join('\n')}

Steps:
1. Run: dotnet build --no-restore (repo root). Capture errors.
2. If build passes: dotnet test --no-build --filter "TestCategory=UnitTests"
3. If build or tests fail:
   a. Revert ONLY the files modified by this batch (git checkout -- <file> for each; git status to confirm — do NOT touch files modified by earlier batches or other pre-existing working-tree changes).
   b. Re-run dotnet build --no-restore to confirm the revert restores green.
   c. Return reverted=true with buildStatus/testStatus reflecting the ORIGINAL failure.
4. If green: return reverted=false.
Do NOT commit or stage anything.`,
        { label: `verify:batch-${b + 1}`, phase: 'Auto-Fix', schema: BATCH_VERIFY_SCHEMA }
      )

      if (batchVerify?.reverted) {
        log(`❌ Batch ${b + 1} failed verification (build: ${batchVerify.buildStatus}, tests: ${batchVerify.testStatus}) — batch reverted and escalated.`)
        for (const r of batchApplied) {
          r.fixResult = { ...r.fixResult, status: 'failed' }
          needsOperator.push({
            ...r,
            changeClass:      'C',
            blockerReason:    `Batch reverted after build/test failure: ${batchVerify.summary}`,
            requiredEvidence: ['Root cause investigation', 'Explicit operator consent', 'Test-first trace (RED→GREEN→REFACTOR)'],
          })
        }
      } else {
        log(`✅ Batch ${b + 1} verified (build + unit tests green).`)
      }
    }
  }

  const applied = fixResults.filter(r => r.fixResult?.status === 'applied')
  const skipped = fixResults.filter(r => r.fixResult?.status === 'skipped')
  const failed  = fixResults.filter(r => r.fixResult?.status === 'failed')

  log(`Auto-fix complete: ${applied.length} applied, ${skipped.length} skipped (escalated), ${failed.length} failed`)

  // ── Step 14 — Verify ──────────────────────────────────────────────────
  phase('Verify')

  const VERIFY_SCHEMA = {
    type: 'object',
    properties: {
      buildStatus:   { type: 'string', enum: ['passed', 'failed'] },
      testStatus:    { type: 'string', enum: ['passed', 'failed', 'skipped'] },
      buildErrors:   { type: 'array', items: { type: 'string' } },
      testFailures:  { type: 'array', items: { type: 'string' } },
      revertedFixes: { type: 'array', items: { type: 'string' } },
      summary:       { type: 'string' },
    },
    required: ['buildStatus', 'testStatus', 'summary'],
  }

  let verifyResult
  if (applied.length === 0) {
    verifyResult = { buildStatus: 'passed', testStatus: 'skipped', buildErrors: [], testFailures: [], revertedFixes: [], summary: 'No fixes applied — verification skipped.' }
    log('No applied fixes to verify.')
  } else {
    verifyResult = await agent(
      `Verify that all auto-applied architecture fixes compile and pass tests.

Applied fix IDs: ${applied.map(r => r.id).join(', ')}

Modified files:
${applied.flatMap(r => r.fixResult?.filesModified ?? []).join('\n')}

Each batch of fixes was already build + unit-test verified when it was applied; this
is the final full-suite gate across all batches combined.

Steps:
1. Run: dotnet build --no-restore (repo root). Capture errors.
2. If build passes: dotnet test --no-build (full suite).
3. If build or tests fail:
   a. Identify which fix(es) caused the failure (git diff the modified files).
   b. For each breaking fix: restore the original file content (git checkout -- <file>) — touch ONLY files listed above, never other working-tree changes.
   c. Record the reverted fix IDs in revertedFixes.
   d. Re-run build (and the failing tests) to confirm the revert restored green.
4. Return final build/test status. Do NOT commit or stage anything.`,
      { label: 'verify:post-fix', phase: 'Verify', schema: VERIFY_SCHEMA }
    )

    log(verifyResult?.buildStatus === 'passed' ? `✅ Build passed` : `❌ Build failed — reverted: ${(verifyResult?.revertedFixes ?? []).join(', ') || 'none'}`)
    if (verifyResult?.testStatus === 'passed') log(`✅ Tests passed`)
    else if (verifyResult?.testStatus === 'failed') log(`❌ Tests failed — ${(verifyResult?.testFailures ?? []).length} failures`)
  }

  for (const revertedId of (verifyResult?.revertedFixes ?? [])) {
    const original = autoFixable.find(f => f.id === revertedId)
    if (original) {
      needsOperator.push({
        ...original,
        changeClass:      'C',
        blockerReason:    'Reverted after build/test failure — requires architectural review before re-applying',
        requiredEvidence: ['Root cause investigation', 'Explicit operator consent', 'Test-first trace (RED→GREEN→REFACTOR)'],
      })
    }
  }

  const finalApplied = applied.filter(r => !(verifyResult?.revertedFixes ?? []).includes(r.id))

  // ── Step 15 — Commit ──────────────────────────────────────────────────
  phase('Commit')

  const verificationPassed = verifyResult?.buildStatus === 'passed' &&
    (verifyResult?.testStatus === 'passed' || verifyResult?.testStatus === 'skipped')

  if (!verificationPassed && finalApplied.length > 0) {
    log(`⛔ Verification failed (build: ${verifyResult?.buildStatus}, tests: ${verifyResult?.testStatus}) — skipping commit to prevent merging broken changes.`)
    needsOperator.push(...finalApplied.map(r => ({
      ...r,
      changeClass:      'C',
      blockerReason:    'Auto-fix left build or tests failing — requires operator review before committing',
      requiredEvidence: ['Root cause investigation', 'Explicit operator consent', 'Test-first trace (RED→GREEN→REFACTOR)'],
    })))
  } else if (verificationPassed && finalApplied.length > 0) {
    // Operator policy: the workflow never commits. Verified fixes are left
    // uncommitted in the working tree for operator review and commit.
    log(`✅ ${finalApplied.length} verified auto-fixes left uncommitted in the working tree for operator review:`)
    for (const r of finalApplied)
      log(`   [${r.id}] ${r.title}`)
  } else {
    log('No fixes applied.')
  }

  // ── Step 16 — Final report ────────────────────────────────────────────
  phase('Final Report')

  const FINAL_REPORT_SCHEMA = {
    type: 'object',
    properties: {
      reportPath:         { type: 'string' },
      totalFindings:      { type: 'number' },
      autoFixableCount:   { type: 'number' },
      needsOperatorCount: { type: 'number' },
      deepeningOnlyCount: { type: 'number' },
      summary:            { type: 'string' },
    },
    required: ['reportPath', 'totalFindings', 'autoFixableCount', 'needsOperatorCount', 'deepeningOnlyCount', 'summary'],
  }

  const reportResult = await agent(
    `Update analysis/archcheck/report.md with the outcomes of the auto-fix and verify phases.

Read the current analysis/archcheck/report.md first, then update it in place.

Fix outcomes:
  Applied and verified (${finalApplied.length}): ${JSON.stringify(finalApplied.map(r => ({ id: r.id, title: r.title, files: r.fixResult?.filesModified })))}
  Skipped/escalated (${skipped.length}): ${JSON.stringify(skipped.map(r => ({ id: r.id, skipReason: r.fixResult?.skipReason })))}
  Failed (${failed.length}): ${JSON.stringify(failed.map(r => ({ id: r.id, summary: r.fixResult?.summary })))}
  Reverted: ${JSON.stringify(verifyResult?.revertedFixes ?? [])}

Build: ${verifyResult?.buildStatus ?? 'N/A'}
Tests: ${verifyResult?.testStatus ?? 'N/A'}
Build errors: ${JSON.stringify(verifyResult?.buildErrors ?? [])}
Test failures: ${JSON.stringify(verifyResult?.testFailures ?? [])}

Needs operator (final, ${needsOperator.length} items): ${JSON.stringify(needsOperator)}
Deepening only (${deepeningOnly.length} items): ${JSON.stringify(deepeningOnly)}

Updates to make:

1. Rebuild "⚠️ OPERATOR ACTION REQUIRED" section from the final needsOperator list (before the summary table). Per item: ID, title, file:line, Class C blocker reason (exact quote from governance doc), required evidence checklist, fix pending consent.

2. Summary table — add a "Status" column:
   ✅ Fixed | 🔄 Escalated | ⏳ Pending operator | 📋 Backlog | 🔬 Deepening

3. Each violation entry — add status badge. For fixed items add:
   Fixed in: <commit SHA>
   Files modified: <list>

4. Build and Test Results section — append after the summary table.

5. Recommended Next Steps — one action per needsOperator item with evidence required.

Return: reportPath, counts, one-paragraph summary suitable for a PR description.`,
    { label: 'report:final', phase: 'Final Report', schema: FINAL_REPORT_SCHEMA }
  )

  // ── Terminal summary ───────────────────────────────────────────────────
  const totalFixed     = finalApplied.length
  const totalEscalated = needsOperator.length
  const totalDeepening = deepeningOnly.length
  const totalFound     = autoFixable.length + needsOperator.length + deepeningOnly.length

  log(`
## Architecture Review — Final Summary (${mode} mode)

| | Count |
|---|---|
| Total findings | ${totalFound} |
| ✅ Auto-fixed & verified | ${totalFixed} |
| ⚠️ Needs operator (Class C) | ${totalEscalated} |
| 🔬 Deepening opportunities | ${totalDeepening} |
| ❌ Fix failures | ${failed.length} |

${totalEscalated > 0 ? `
### ⚠️ Operator Action Required

${needsOperator.map(f => `**[${f.id}]** ${f.title}
  Reason: ${f.blockerReason}
  Evidence needed: ${(f.requiredEvidence ?? []).join(', ')}`).join('\n\n')}
` : '### ✅ No Class C items — no operator action required'}

Build: ${verifyResult?.buildStatus ?? 'N/A'} | Tests: ${verifyResult?.testStatus ?? 'N/A'}

Full report: \`${reportResult?.reportPath ?? 'analysis/archcheck/report.md'}\`
${reportResult?.summary ?? ''}
`)

  return {
    mode,
    totalFound,
    totalFixed,
    totalEscalated,
    totalDeepening,
    fixFailed:   failed.length,
    buildStatus: verifyResult?.buildStatus ?? 'N/A',
    testStatus:  verifyResult?.testStatus  ?? 'N/A',
    needsOperator: needsOperator.map(f => ({ id: f.id, title: f.title, blockerReason: f.blockerReason })),
    reportPath: reportResult?.reportPath ?? 'analysis/archcheck/report.md',
  }
}
