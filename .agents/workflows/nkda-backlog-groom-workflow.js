export const meta = {
  name: 'nkda-backlog-groom-workflow',
  description: 'Pre-start assess every needs-assessment Backlog Item; report verdicts, or (apply) label, raise Decisions, and break too-big items into sub-issues, recursing into new slices.',
  whenToUse: 'Before delivery, to make sure every Backlog Item is ready and right-sized. args: { mode: "report" | "apply", issues?: number[], maxDepth?: number }',
  phases: [
    { title: 'Select',  detail: 'List open needs-assessment Backlog Items (or the issues passed in args)' },
    { title: 'Assess',  detail: 'Execute skill: .agents/skills/nkda-backlog-item-prestart/SKILL.md (assess mode), one agent per item' },
    { title: 'Apply',   detail: 'Execute the same skill in apply mode: labels, Decisions, sub-issues (apply mode only)' },
    { title: 'Report',  detail: 'Write analysis/backlog/groom-report.md' },
  ],
}

// args: { mode: 'report' | 'apply', issues?: number[], maxDepth?: number }
// report — read-only; proposes verdicts and slices.
// apply  — acts on GitHub under the standing authorisation in .agents/10-contracts/consent-policy.yaml.
const opts     = (args && typeof args === 'object') ? args : { mode: typeof args === 'string' ? args : 'report' }
const mode     = (opts.mode ?? 'report').toLowerCase() === 'apply' ? 'apply' : 'report'
const maxDepth = opts.maxDepth ?? 3
log(`Mode: ${mode}; max breakdown depth: ${maxDepth}`)

const LIST_SCHEMA = {
  type: 'object',
  properties: { issues: { type: 'array', items: { type: 'object', properties: {
    number: { type: 'number' }, title: { type: 'string' } }, required: ['number', 'title'] } } },
  required: ['issues'],
}

const PRESTART_SCHEMA = {
  type: 'object',
  properties: {
    issue:   { type: 'number' },
    verdict: { type: 'string', enum: ['ready', 'blocked', 'too-big', 'skip'] },
    reasons: { type: 'array', items: { type: 'string' } },
    proposedDecisions: { type: 'array', items: { type: 'object', properties: {
      title: { type: 'string' }, question: { type: 'string' } }, required: ['title', 'question'] } },
    slices: { type: 'array', items: { type: 'object', properties: {
      title: { type: 'string' }, acceptanceCriteria: { type: 'array', items: { type: 'string' } },
      likelyTooBig: { type: 'boolean' } }, required: ['title', 'acceptanceCriteria'] } },
    dropped: { type: 'array', items: { type: 'object', properties: {
      criterion: { type: 'string' }, reason: { type: 'string' } }, required: ['criterion', 'reason'] } },
    areas:   { type: 'array', items: { type: 'string' } },
    applied: { type: 'boolean' },
    createdIssues: { type: 'array', items: { type: 'number' } },
  },
  required: ['issue', 'verdict', 'reasons', 'areas', 'applied'],
}

const prestartPrompt = (n, m) => `Read and execute the skill exactly as documented in:
  .agents/skills/nkda-backlog-item-prestart/SKILL.md

Issue: #${n}
Mode: ${m}
${m === 'apply'
  ? 'Apply mode is authorised by the standing authorisation "backlog-automation" in .agents/10-contracts/consent-policy.yaml. Create sub-issues with gh issue create --parent and keep the parent open, labelled broken-down. Return createdIssues with the numbers of every issue you created (slices and Decisions).'
  : 'Assess mode: read-only. Do NOT modify GitHub.'}
Return the skill's output schema.`

// ── Select ────────────────────────────────────────────────────────────────
phase('Select')
let queue
if (Array.isArray(opts.issues) && opts.issues.length) {
  queue = opts.issues.map(n => ({ number: n, title: `#${n}` }))
} else {
  const listed = await agent(
    `Run: gh issue list --state open --label needs-assessment --label backlog-item --limit 200 --json number,title
Return every issue as {number, title}. Read-only.`,
    { label: 'select:needs-assessment', phase: 'Select', schema: LIST_SCHEMA, effort: 'low' })
  queue = listed?.issues ?? []
}
log(`${queue.length} item(s) to assess`)

// ── Assess (+ Apply), recursing into newly created slices ─────────────────
const results = []
const seen = new Set()
for (let depth = 0; depth <= maxDepth && queue.length; depth++) {
  const batch = queue.filter(i => !seen.has(i.number))
  batch.forEach(i => seen.add(i.number))
  if (!batch.length) break
  log(`Depth ${depth}: assessing ${batch.length} item(s)`)

  const round = await pipeline(batch,
    item => agent(prestartPrompt(item.number, 'assess'),
      { label: `assess:#${item.number}`, phase: 'Assess', schema: PRESTART_SCHEMA }),
    assessed => {
      if (!assessed || mode !== 'apply' || assessed.verdict === 'skip') return assessed
      return agent(prestartPrompt(assessed.issue, 'apply'),
        { label: `apply:#${assessed.issue}`, phase: 'Apply', schema: PRESTART_SCHEMA })
    })

  const done = round.filter(Boolean)
  results.push(...done.map(r => ({ ...r, depth })))
  // In report mode nothing is created, so there is nothing to recurse into.
  queue = mode === 'apply'
    ? done.filter(r => r.verdict === 'too-big').flatMap(r => (r.createdIssues ?? []).map(n => ({ number: n, title: `#${n}` })))
    : []
  if (queue.length && depth === maxDepth)
    log(`⚠️ Max depth ${maxDepth} reached — ${queue.length} new slice(s) left labelled needs-assessment: ${queue.map(q => '#' + q.number).join(', ')}`)
}

const count = v => results.filter(r => r.verdict === v).length
log(`Verdicts: ${count('ready')} ready, ${count('blocked')} blocked, ${count('too-big')} too big, ${count('skip')} skipped`)
const dropped = results.length - seen.size
if (dropped < 0) log(`⚠️ ${-dropped} assessment(s) returned no result — re-run for those issues.`)

// ── Report ────────────────────────────────────────────────────────────────
phase('Report')
await agent(
  `Write analysis/backlog/groom-report.md summarising this grooming run (mode: ${mode}).

Sections:
1. Summary counts by verdict.
2. Decisions needed from the operator (every proposedDecisions entry, with its issue).
3. Ready items (issue, title, areas).
4. Blocked items with reasons.
5. Too-big items with their slices and any dropped criteria${mode === 'report' ? ' (PROPOSED — not created; run with mode "apply" to create)' : ' (created sub-issue numbers)'}.

Results:
${JSON.stringify(results, null, 2)}`,
  { label: 'report:groom', phase: 'Report', effort: 'low' })

return {
  mode,
  assessed: results.length,
  ready:   results.filter(r => r.verdict === 'ready').map(r => r.issue),
  blocked: results.filter(r => r.verdict === 'blocked').map(r => ({ issue: r.issue, reasons: r.reasons })),
  tooBig:  results.filter(r => r.verdict === 'too-big').map(r => ({ issue: r.issue, slices: (r.createdIssues ?? r.slices?.map(s => s.title)) })),
  decisionsNeeded: results.flatMap(r => (r.proposedDecisions ?? []).map(d => ({ issue: r.issue, ...d }))),
  reportPath: 'analysis/backlog/groom-report.md',
}
