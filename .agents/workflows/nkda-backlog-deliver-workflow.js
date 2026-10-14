export const meta = {
  name: 'nkda-backlog-deliver-workflow',
  description: 'Deliver ready Backlog Items in parallel worktrees: implement test-first, run the completion protocol with an isolated live E2E per worktree, open a PR, independently verify, and set auto-merge by confidence.',
  whenToUse: 'After grooming, to work through ready Backlog Items. args: { slots?: number (default 3), maxItems?: number (default 6), issues?: number[], dryRun?: boolean }',
  phases: [
    { title: 'Select',  detail: 'Ready Backlog Items whose dependencies are merged, in priority order, with their code areas' },
    { title: 'Deliver', detail: 'One worktree per item: re-check ready, provision adpe2e-<issue>-<slot> projects, implement test-first, completion protocol steps 0–6, PR, report' },
    { title: 'Verify',  detail: 'Independent reviewer per PR: re-checks the evidence and the original ask, then sets auto-merge on (≥90% and all steps pass) or off' },
    { title: 'Report',  detail: 'Write analysis/backlog/deliver-report.md' },
  ],
}

// Rules this workflow relies on:
//   .agents/20-guardrails/workflow/backlog-item-workflow.md   (pre-start, completion protocol, report, auto-merge)
//   .agents/20-guardrails/workflow/live-test-environment.md   (orgs, auth, adpe2e-<run-id> isolation, guards)
//   .agents/10-contracts/consent-policy.yaml                  (standing authorisation "backlog-automation")
const opts     = (args && typeof args === 'object') ? args : {}
const SLOTS    = Math.max(1, Math.min(opts.slots ?? 3, 4))
const MAX      = opts.maxItems ?? 6
const DRY      = !!opts.dryRun
log(`Slots: ${SLOTS}; max items this run: ${MAX}${DRY ? '; DRY RUN (no implementation)' : ''}`)

const SELECT_SCHEMA = {
  type: 'object',
  properties: { items: { type: 'array', items: { type: 'object', properties: {
    number: { type: 'number' }, title: { type: 'string' },
    areas: { type: 'array', items: { type: 'string' } },
    eligible: { type: 'boolean' }, reason: { type: 'string' } },
    required: ['number', 'title', 'areas', 'eligible', 'reason'] } } },
  required: ['items'],
}

const DELIVER_SCHEMA = {
  type: 'object',
  properties: {
    issue:      { type: 'number' },
    outcome:    { type: 'string', enum: ['pr-opened', 'blocked', 'failed', 'not-ready'] },
    prNumber:   { type: 'number' },
    branch:     { type: 'string' },
    steps: { type: 'array', items: { type: 'object', properties: {
      step: { type: 'string' }, status: { type: 'string', enum: ['pass', 'fail', 'blocked', 'n/a'] }, evidence: { type: 'string' } },
      required: ['step', 'status', 'evidence'] } },
    confidence: { type: 'number' },
    weakestEvidence: { type: 'string' },
    notDone:    { type: 'array', items: { type: 'string' } },
    decisionsRaised: { type: 'array', items: { type: 'number' } },
    cleanup:    { type: 'string' },
    summary:    { type: 'string' },
  },
  required: ['issue', 'outcome', 'steps', 'confidence', 'notDone', 'cleanup', 'summary'],
}

const VERIFY_SCHEMA = {
  type: 'object',
  properties: {
    prNumber:   { type: 'number' },
    agreesWithReport: { type: 'boolean' },
    discrepancies: { type: 'array', items: { type: 'string' } },
    unmetCriteria: { type: 'array', items: { type: 'string' } },
    verifiedConfidence: { type: 'number' },
    autoMerge:  { type: 'string', enum: ['enabled', 'disabled'] },
    reason:     { type: 'string' },
  },
  required: ['prNumber', 'agreesWithReport', 'discrepancies', 'unmetCriteria', 'verifiedConfidence', 'autoMerge', 'reason'],
}

// ── Select ────────────────────────────────────────────────────────────────
phase('Select')
const selected = await agent(
  `Select Backlog Items to deliver. Read-only.

${Array.isArray(opts.issues) && opts.issues.length
  ? `Candidates: exactly these issues: ${opts.issues.map(n => '#' + n).join(', ')}.`
  : 'Candidates: gh issue list --state open --label ready --label backlog-item --limit 100 --json number,title,body,labels'}

For each candidate:
- eligible=false if it lacks the "ready" label, has "blocked" or "broken-down", or any "Depends on" issue is not closed-as-completed with its PR merged to main (check with gh).
- areas = the code areas it will touch: top-level src/ or tests/ project names, plus shared files (README.md, docs/configuration-reference.md, .agents/10-contracts/routing-catalog.yaml, Directory.Packages.props). Use the latest pre-start assessment comment if it lists them; otherwise estimate from the issue and .agents/10-contracts/routing-catalog.yaml first_surfaces.
- Order: oldest issue number first, unless an issue is labelled priority/high.`,
  { label: 'select:ready', phase: 'Select', schema: SELECT_SCHEMA })

const eligible = (selected?.items ?? []).filter(i => i.eligible)
const skipped  = (selected?.items ?? []).filter(i => !i.eligible)
skipped.forEach(i => log(`⏭ #${i.number} not eligible — ${i.reason}`))
const work = eligible.slice(0, MAX)
if (eligible.length > MAX) log(`⚠️ ${eligible.length - MAX} eligible item(s) left for a later run (maxItems=${MAX}): ${eligible.slice(MAX).map(i => '#' + i.number).join(', ')}`)
log(`${work.length} item(s) to deliver`)

// ── Per-item chain: deliver in a worktree, then independent verify ────────
const deliverPrompt = (item, slot) => `You are delivering Backlog Item #${item.number} ("${item.title}") in your own git worktree.
Slot: ${slot}. Run id: ${item.number}-${slot}.

Authorisation: the standing authorisation "backlog-automation" in .agents/10-contracts/consent-policy.yaml permits you to commit, push a branch, open a PR, comment on and label issues for THIS item only. Do not touch other issues or branches.

Follow, in order:
1. Re-check: gh issue view ${item.number}. If it is no longer labelled ready, or its dependencies are not merged, stop with outcome=not-ready.
2. Branch: create backlog/${item.number}-<short-slug> from up-to-date origin/main.
3. Live environment, per .agents/20-guardrails/workflow/live-test-environment.md:
   - preflight both orgs; on failure, outcome=blocked ("live environment unavailable") — do not continue to implementation.
   - create projects adpe2e-${item.number}-${slot}-src (source org) and adpe2e-${item.number}-${slot}-tgt (target org) via the ADO REST API, honouring the adpe2e- prefix and org guards; seed the source per the scenario.
   - set SOURCE_PROJECT / TARGET_PROJECT / DEVOPS_MIGRATION_TEST_STORAGE (%TEMP%\\adpe2e\\${item.number}-${slot}) for your process only; Control Plane on port ${5100 + slot} via a run-local config copy.
   - credentials come only from the inherited environment; never print, write, or commit them.
4. Implement test-first: follow the repository's routing (CLAUDE.md "Routing Is Contract-Driven") and .agents/20-guardrails/workflow/test-first-workflow.md — RED → GREEN → REFACTOR for every behaviour change. If the item has a specs/ folder, follow its tasks.md.${DRY ? '\n   DRY RUN: do NOT implement. Stop after step 3, clean up, and return outcome=blocked with summary "dry run".' : ''}
5. If you hit a question that needs the operator: raise a Decision via .agents/skills/nkda-backlog-item-create/SKILL.md (create mode), label #${item.number} blocked, clean up, and return outcome=blocked with decisionsRaised.
6. Completion protocol §A steps 0–6 of backlog-item-workflow.md, every step, recording evidence per step (step 3 = live E2E against your adpe2e-${item.number}-${slot} projects, judged by live-test-environment.md §6). Write the session evidence file required by definition-of-done.md §9.
7. Commit (end messages with "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"), push the branch, and open a PR with the repository PR template fully filled in (completion report §B: what, how tested, docs, confidence %, not done), "Closes #${item.number}", and "🤖 Generated with [Claude Code](https://claude.com/claude-code)" at the end. Do NOT enable auto-merge — the verifier decides.
8. Step 6 cleanup: delete only adpe2e-${item.number}-${slot}-* projects (and purge from the recycle bin), the storage folder, and scratch files. Report anything left behind.
9. Comment on #${item.number} with the PR link and the confidence figure.

Return the output schema. Be honest: confidence reflects the weakest evidence; any failed or blocked step means outcome is not "pr-opened" unless the PR is opened as a draft with the failure stated.`

const verifyPrompt = (item, d) => `Independently verify PR #${d.prNumber} for Backlog Item #${item.number}. You did not write it; be sceptical.

Authorisation: standing authorisation "backlog-automation" in .agents/10-contracts/consent-policy.yaml permits you to set auto-merge on this PR only.

1. gh pr view ${d.prNumber} and gh issue view ${item.number}: compare the delivered change against every acceptance criterion and the Backlog Item DoD (.agents/20-guardrails/workflow/backlog-item-workflow.md §C). List unmet criteria.
2. Check out the PR branch and check the claimed evidence is real: build, run the full test suite, confirm the RED → GREEN trace and the session evidence file validate (python scripts/guardrails/validate-evidence.py <file>). Do not re-run the live E2E; check its recorded observations are specific (counts, sampled items), not generic.
3. Check for scope creep and for Constitution violations in the diff.
4. Author's report: ${JSON.stringify({ confidence: d.confidence, steps: d.steps, notDone: d.notDone, weakestEvidence: d.weakestEvidence })}
5. Decide verifiedConfidence. Auto-merge rule: enable ONLY if verifiedConfidence ≥ 90 AND every completion step passed AND no unmet criteria:
     gh pr merge ${d.prNumber} --auto --squash
   otherwise: gh pr merge ${d.prNumber} --disable-auto (ignore the error if it was never enabled).
6. Post a PR comment with your verdict, discrepancies, and the auto-merge decision.

Return the output schema.`

const results = []
let nextSlot = 1
const freeSlots = []
const takeSlot = () => freeSlots.length ? freeSlots.shift() : nextSlot++
const running = new Map()            // number -> { promise, areas, slot }
const pending = [...work]
const overlaps = (a, b) => a.some(x => b.includes(x))

const runItem = async (item, slot) => {
  log(`▶ #${item.number} in slot ${slot} (areas: ${item.areas.join(', ') || 'unknown'})`)
  const d = await agent(deliverPrompt(item, slot),
    { label: `deliver:#${item.number}`, phase: 'Deliver', schema: DELIVER_SCHEMA, isolation: 'worktree', effort: 'high' })
  if (!d) return { issue: item.number, outcome: 'failed', summary: 'agent returned no result' }
  log(`${d.outcome === 'pr-opened' ? '✅' : '⛔'} #${item.number}: ${d.outcome}${d.prNumber ? ` — PR #${d.prNumber}` : ''} (self-assessed ${d.confidence}%)`)
  if (d.outcome !== 'pr-opened' || !d.prNumber) return d
  const v = await agent(verifyPrompt(item, d),
    { label: `verify:PR#${d.prNumber}`, phase: 'Verify', schema: VERIFY_SCHEMA, isolation: 'worktree', effort: 'high' })
  log(`🔎 PR #${d.prNumber}: verified ${v?.verifiedConfidence ?? '?'}% — auto-merge ${v?.autoMerge ?? 'unknown'}`)
  return { ...d, verify: v }
}

// Scheduler: at most SLOTS concurrent items, never two with overlapping areas.
while (pending.length || running.size) {
  let started = false
  for (let i = 0; i < pending.length && running.size < SLOTS; ) {
    const item = pending[i]
    const busy = [...running.values()].some(r => overlaps(r.areas, item.areas))
    if (busy) { i++; continue }
    pending.splice(i, 1)
    const slot = takeSlot()
    const promise = runItem(item, slot).then(r => ({ number: item.number, slot, r }))
    running.set(item.number, { promise, areas: item.areas, slot })
    started = true
  }
  if (!running.size) {
    // Only reachable if every pending item conflicts with nothing running — cannot happen; guard anyway.
    log(`⚠️ Scheduler stalled with ${pending.length} pending item(s); stopping.`)
    break
  }
  const finished = await Promise.race([...running.values()].map(r => r.promise))
  running.delete(finished.number)
  freeSlots.push(finished.slot)
  results.push(finished.r)
  if (!started && pending.length) log(`… ${pending.length} item(s) waiting on area conflicts`)
}

// ── Report ────────────────────────────────────────────────────────────────
phase('Report')
await agent(
  `Write analysis/backlog/deliver-report.md for this delivery run.

Sections:
1. Summary: delivered (PR opened), auto-merge enabled, auto-merge disabled (with reason), blocked, failed, not ready.
2. Decisions waiting for the operator (decisionsRaised).
3. Per item: issue, PR, self-assessed vs verified confidence, discrepancies, not-done list, cleanup result.
4. Skipped as ineligible: ${JSON.stringify(skipped.map(s => ({ issue: s.number, reason: s.reason })))}
5. Next steps: items unblocked once these PRs merge; re-run nkda-backlog-groom-workflow for any new needs-assessment items.

Results:
${JSON.stringify(results, null, 2)}`,
  { label: 'report:deliver', phase: 'Report', effort: 'low' })

return {
  delivered: results.filter(r => r?.outcome === 'pr-opened').map(r => ({ issue: r.issue, pr: r.prNumber, autoMerge: r.verify?.autoMerge, verifiedConfidence: r.verify?.verifiedConfidence })),
  blocked:   results.filter(r => r?.outcome === 'blocked').map(r => ({ issue: r.issue, summary: r.summary, decisions: r.decisionsRaised })),
  failed:    results.filter(r => r?.outcome === 'failed' || r?.outcome === 'not-ready').map(r => ({ issue: r.issue, outcome: r.outcome, summary: r.summary })),
  leftForLater: eligible.slice(MAX).map(i => i.number),
  reportPath: 'analysis/backlog/deliver-report.md',
}
