export const meta = {
  name: 'nkda-backlog-deliver-workflow',
  description: 'Deliver ready Backlog Items, one worktree and one PR each: claim, implement test-first, run the completion protocol with a live E2E, open a PR, independently verify, and apply the auto-merge gate in code.',
  whenToUse: 'After grooming. args: { runId: string (required, unique per invocation, e.g. a timestamp), slots?: number (default 1), maxItems?: number (default 6), issues?: number[], dryRun?: boolean, isolatedLive?: boolean }',
  phases: [
    { title: 'Select',  detail: 'Open Backlog Items labelled ready (not blocked/broken-down/in-progress) whose dependencies are merged to main' },
    { title: 'Deliver', detail: 'One worktree per item: re-check eligibility, claim (assign + in-progress), implement test-first, completion protocol, PR' },
    { title: 'Verify',  detail: 'Independent reviewer per non-draft PR re-checks the evidence and the original ask' },
    { title: 'Merge gate', detail: 'Auto-merge is enabled only when the verifier confidence is at least 90, every step passed and no criterion is unmet (computed here, not by an agent)' },
    { title: 'Report',  detail: 'Write analysis/backlog/deliver-report.md' },
  ],
}

// Rules: .agents/20-guardrails/workflow/backlog-item-workflow.md (one task = one PR = one worktree, pre-start),
//        completion-protocol.md (steps 0–6, report, auto-merge rule), backlog-item-dod.md (L3),
//        live-test-environment.md (Entra token as PAT, run ids, isolation), .agents/10-contracts/consent-policy.yaml.
const opts = (args && typeof args === 'object') ? args : {}
if (!opts.runId || typeof opts.runId !== 'string')
  throw new Error('args.runId is required: a value unique to this invocation (e.g. "20261015T1530"). It names live-test resources, so it must never be reused.')
const RUN = opts.runId.replace(/[^A-Za-z0-9-]/g, '').toLowerCase()
// Until #176 moves live scenarios to per-run projects, the live suite shares fixed projects, so live
// runs are serialised (one slot). Pass isolatedLive=true only once per-run projects exist.
const SLOTS = opts.isolatedLive ? Math.max(1, Math.min(opts.slots ?? 3, 4)) : 1
const MAX = opts.maxItems ?? 6
const DRY = !!opts.dryRun
log(`Run ${RUN}; slots: ${SLOTS}${opts.isolatedLive ? '' : ' (serialised: live tests share fixed projects until #176)'}; max items: ${MAX}${DRY ? '; DRY RUN' : ''}`)

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
    outcome:    { type: 'string', enum: ['pr-opened', 'pr-draft', 'blocked', 'failed', 'not-ready'] },
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
    reason:     { type: 'string' },
  },
  required: ['prNumber', 'agreesWithReport', 'discrepancies', 'unmetCriteria', 'verifiedConfidence', 'reason'],
}

const ELIGIBILITY = `An item is eligible only if ALL hold: the issue is OPEN; it has labels backlog-item and ready; it has none of blocked, broken-down, in-progress; and every "Depends on" issue is closed as completed AND its delivering PR is merged into main (check mergedAt and baseRefName=main, not just the issue state).`

// ── Select ────────────────────────────────────────────────────────────────
phase('Select')
const selected = await agent(
  `Select Backlog Items to deliver. Read-only.

${Array.isArray(opts.issues) && opts.issues.length
  ? `Candidates: exactly these issues: ${opts.issues.map(n => '#' + n).join(', ')}. Load each with gh issue view <n> --json number,title,state,labels,body — explicitly listed issues must still pass every eligibility rule (closed issues are NOT eligible, even if still labelled ready).`
  : 'Candidates: every open issue labelled ready and backlog-item: gh api --paginate "repos/{owner}/{repo}/issues?state=open&labels=ready,backlog-item&per_page=100".'}

${ELIGIBILITY}
For each candidate set eligible and reason. areas = code areas it will touch: top-level src/ or tests/ projects plus shared files (README.md, docs/configuration-reference.md, .agents/10-contracts/routing-catalog.yaml, Directory.Packages.props, .github/workflows/*). Use the latest pre-start assessment comment if it lists them. Order oldest first unless labelled priority/high.`,
  { label: 'select:ready', phase: 'Select', schema: SELECT_SCHEMA })

const eligible = (selected?.items ?? []).filter(i => i.eligible)
const skipped  = (selected?.items ?? []).filter(i => !i.eligible)
skipped.forEach(i => log(`⏭ #${i.number} not eligible — ${i.reason}`))
const work = eligible.slice(0, MAX)
if (eligible.length > MAX) log(`⚠️ ${eligible.length - MAX} eligible item(s) left for a later run (maxItems=${MAX}): ${eligible.slice(MAX).map(i => '#' + i.number).join(', ')}`)
log(`${work.length} item(s) to deliver`)

// ── Prompts ───────────────────────────────────────────────────────────────
const deliverPrompt = (item, runId) => `You are delivering Backlog Item #${item.number} ("${item.title}") in your own fresh git worktree, through one PR. Run id: ${runId} (unique; never reuse it).

Authorisation: standing authorisation "backlog-automation" (.agents/10-contracts/consent-policy.yaml on origin/main) lets you claim, comment on and label THIS issue, commit, push a branch and open a PR for it. Verify it is present on origin/main first; if not, stop with outcome=blocked. Do not touch other issues or branches.

1. Re-check eligibility NOW, immediately before doing anything: ${ELIGIBILITY} If any rule fails, stop with outcome=not-ready.
2. Claim: gh issue edit ${item.number} --add-assignee @me --add-label in-progress. Remove in-progress when you finish, whatever the outcome.
3. Branch backlog/${item.number}-<short-slug> from up-to-date origin/main.
4. Live environment per .agents/20-guardrails/workflow/live-test-environment.md: Entra token used as the PAT (AZDEVOPS_SYSTEM_TEST_PAT, and SOURCE_PAT/TARGET_PAT for per-run copies), set for your test processes only and never printed or written; organisations and run id ${runId}. Preflight first; on failure stop with outcome=blocked ("live environment unavailable"). Per-run scenario copies must have every endpoint rewritten and the resolved config validated before running.
5. Implement test-first per .agents/20-guardrails/workflow/test-first-workflow.md (RED → GREEN → REFACTOR). Follow the repo routing (CLAUDE.md).${DRY ? '\n   DRY RUN: do NOT implement. Stop after step 4, clean up, and return outcome=blocked with summary "dry run".' : ''}
6. If a question needs the operator: raise or reuse a Decision via .agents/skills/nkda-backlog-item-create/SKILL.md, set the item's labels to blocked (removing ready), clean up, and return outcome=blocked with decisionsRaised.
7. Run the completion protocol (.agents/20-guardrails/workflow/completion-protocol.md §A steps 0–6), every step, recording evidence per step. Run every test command with a hard timeout. Write the session evidence file (definition-of-done.md §9).
8. Commit (end messages with "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"), push, and open the PR with the template filled in (completion report §B), "Closes #${item.number}", ending with "🤖 Generated with [Claude Code](https://claude.com/claude-code)". If any step failed or is blocked, open it as a DRAFT and return outcome=pr-draft. Never enable auto-merge yourself.
9. Clean up (step 6): only adpe2e-${runId}-* resources, your storage folder, scratch files and stray processes.
10. Comment on #${item.number} with the PR link and your confidence.

Return the output schema. Confidence reflects your weakest evidence.`

const verifyPrompt = (item, d) => `Independently verify PR #${d.prNumber} for Backlog Item #${item.number}. You did not write it; be sceptical. Read-only: do NOT change auto-merge, labels or the PR — the workflow applies the merge gate.

1. Compare the change with every acceptance criterion and the Backlog Item DoD (.agents/20-guardrails/workflow/backlog-item-dod.md). List unmet criteria.
2. In your own worktree, check out the PR branch and confirm the evidence is real: build; run the full non-live suite by TestCategory with a hard timeout; confirm the RED → GREEN trace; validate the session evidence (python scripts/guardrails/validate-evidence.py <file>). Do not re-run live tests; check their recorded observations are specific (counts, sampled items).
3. Check for scope creep and Constitution violations.
4. Author's report: ${JSON.stringify({ confidence: d.confidence, steps: d.steps, notDone: d.notDone, weakestEvidence: d.weakestEvidence })}
5. Post one PR comment with your verdict and discrepancies.
Return the output schema.`

// ── Per-item chain ────────────────────────────────────────────────────────
const gateAllows = (d, v) =>
  !!v && v.agreesWithReport && v.verifiedConfidence >= 90 && (v.unmetCriteria ?? []).length === 0 &&
  (d.steps ?? []).length > 0 && d.steps.every(s => s.status === 'pass' || s.status === 'n/a')

const runItem = async (item, index) => {
  const runId = `${item.number}-${RUN}`
  try {
    log(`▶ #${item.number} (run ${runId}; areas: ${item.areas.join(', ') || 'unknown'})`)
    const d = await agent(deliverPrompt(item, runId),
      { label: `deliver:#${item.number}`, phase: 'Deliver', schema: DELIVER_SCHEMA, isolation: 'worktree', effort: 'high' })
    if (!d) return { issue: item.number, outcome: 'failed', summary: 'deliver agent returned no result' }
    log(`${d.outcome === 'pr-opened' ? '✅' : '⛔'} #${item.number}: ${d.outcome}${d.prNumber ? ` — PR #${d.prNumber}` : ''} (self-assessed ${d.confidence}%)`)
    if (d.outcome !== 'pr-opened' || !d.prNumber) return d   // drafts and failures are never verified or merged

    const v = await agent(verifyPrompt(item, d),
      { label: `verify:PR#${d.prNumber}`, phase: 'Verify', schema: VERIFY_SCHEMA, isolation: 'worktree', effort: 'high' })
    const allow = gateAllows(d, v)
    await agent(allow
      ? `Run exactly: gh pr merge ${d.prNumber} --auto --squash . Then post a PR comment: "Auto-merge enabled: verifier confidence ${v?.verifiedConfidence}%, all completion steps passed, no unmet criteria." Return "done".`
      : `Run exactly: gh pr merge ${d.prNumber} --disable-auto (ignore an error if it was never enabled). Then post a PR comment: "Auto-merge OFF: ${(v ? `verifier confidence ${v.verifiedConfidence}%, unmet criteria ${(v.unmetCriteria ?? []).length}, agrees with report: ${v.agreesWithReport}` : 'verifier returned no result').replace(/"/g, "'")}; completion steps all passed: ${(d.steps ?? []).every(s => s.status === 'pass' || s.status === 'n/a')}." Return "done".`,
      { label: `gate:PR#${d.prNumber}`, phase: 'Merge gate', effort: 'low' })
    log(`🔎 PR #${d.prNumber}: verified ${v?.verifiedConfidence ?? '?'}% — auto-merge ${allow ? 'ENABLED' : 'off'}`)
    return { ...d, verify: v, autoMerge: allow ? 'enabled' : 'disabled' }
  } catch (err) {
    log(`❌ #${item.number}: ${err?.message ?? err}`)
    return { issue: item.number, outcome: 'failed', summary: `workflow error: ${err?.message ?? err} — check for and clean up adpe2e-${runId}-* resources and the in-progress label` }
  }
}

// Scheduler: at most SLOTS concurrent items, never two with overlapping areas.
const results = []
const running = new Map()
const pending = [...work]
const overlaps = (a, b) => a.some(x => b.includes(x))
let started = 0
while (pending.length || running.size) {
  for (let i = 0; i < pending.length && running.size < SLOTS; ) {
    const item = pending[i]
    if ([...running.values()].some(r => overlaps(r.areas, item.areas))) { i++; continue }
    pending.splice(i, 1)
    const promise = runItem(item, started++).then(r => ({ number: item.number, r }))
    running.set(item.number, { promise, areas: item.areas })
  }
  if (!running.size) { log(`⚠️ Scheduler stalled with ${pending.length} pending item(s); stopping.`); break }
  const finished = await Promise.race([...running.values()].map(r => r.promise))
  running.delete(finished.number)
  results.push(finished.r)
}

// ── Report ────────────────────────────────────────────────────────────────
phase('Report')
await agent(
  `Write analysis/backlog/deliver-report.md for delivery run ${RUN}.
Sections: 1. Summary (PR opened + auto-merge enabled/disabled with reason, draft PRs, blocked, failed, not ready). 2. Decisions waiting for the operator. 3. Per item: issue, PR, self-assessed vs verified confidence, discrepancies, not-done list, cleanup. 4. Skipped as ineligible: ${JSON.stringify(skipped.map(s => ({ issue: s.number, reason: s.reason })))}. 5. Next steps.
Results:
${JSON.stringify(results, null, 2)}`,
  { label: 'report:deliver', phase: 'Report', effort: 'low' })

return {
  runId: RUN,
  delivered: results.filter(r => r?.outcome === 'pr-opened').map(r => ({ issue: r.issue, pr: r.prNumber, autoMerge: r.autoMerge, verifiedConfidence: r.verify?.verifiedConfidence })),
  drafts:    results.filter(r => r?.outcome === 'pr-draft').map(r => ({ issue: r.issue, pr: r.prNumber, summary: r.summary })),
  blocked:   results.filter(r => r?.outcome === 'blocked').map(r => ({ issue: r.issue, summary: r.summary, decisions: r.decisionsRaised })),
  failed:    results.filter(r => r?.outcome === 'failed' || r?.outcome === 'not-ready').map(r => ({ issue: r.issue, outcome: r.outcome, summary: r.summary })),
  leftForLater: eligible.slice(MAX).map(i => i.number),
  reportPath: 'analysis/backlog/deliver-report.md',
}
