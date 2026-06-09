export const meta = {
  name: 'nkda-testdsl-workflow',
  description: 'Migrate Reqnroll feature families to internal DSL — enumerate all files, process each sequentially through the full phase pipeline',
  phases: [
    { title: 'Enumerate', detail: 'Discover all .feature files and filter already-PASS families' },
    { title: 'Assessment', detail: 'Run nkda-testdsl-feature-assessment for the current family' },
    { title: 'DSL Design', detail: 'Run nkda-testdsl-dsl-design for the current family' },
    { title: 'Extraction', detail: 'Run nkda-testdsl-extraction for the current family' },
    { title: 'Conversion', detail: 'Run nkda-testdsl-feature-conversion for the current family' },
    { title: 'Refactor', detail: 'Run nkda-testdsl-refactor for the current family' },
    { title: 'Verification', detail: 'Run nkda-testdsl-verification and commit for the current family' },
  ],
}

// args: feature family name, folder path, or feature file path.
// If omitted, defaults to the canonical feature folder.
const scope = args || 'tests'

// ---------------------------------------------------------------------------
// Phase: Enumerate
// ---------------------------------------------------------------------------
phase('Enumerate')

const ENUMERATE_SCHEMA = {
  type: 'object',
  properties: {
    files: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          featureFilePath: { type: 'string' },
          familyName: { type: 'string' },
          alreadyPassed: { type: 'boolean' },
        },
        required: ['featureFilePath', 'familyName', 'alreadyPassed'],
      },
    },
  },
  required: ['files'],
}

const enumResult = await agent(
  `You are enumerating Reqnroll .feature files for migration scope: "${scope}".

Tasks:
1. Find all .feature files under the scope path (or the single file if a file path was given).
2. For each file, derive the family name (the .feature filename without extension, or use folder-based naming if multiple files share a folder).
3. For each family, check whether .output/nkda-testdsl/<family-name>/06-verification.md exists AND contains a PASS verdict. If both conditions are true, set alreadyPassed=true.
4. Return every file including already-passed ones so the workflow can report them.

Return a structured list.`,
  { label: 'enumerate:scope', phase: 'Enumerate', schema: ENUMERATE_SCHEMA }
)

if (!enumResult || enumResult.files.length === 0) {
  log('No .feature files found in scope. Nothing to do.')
} else {
  const pending = enumResult.files.filter(f => !f.alreadyPassed)
  const skipped = enumResult.files.filter(f => f.alreadyPassed)

  if (skipped.length > 0) {
    log(`Skipping ${skipped.length} already-PASS families: ${skipped.map(f => f.familyName).join(', ')}`)
  }

  if (pending.length === 0) {
    log('All families already have PASS verdicts. Nothing to migrate.')
  } else {
    log(`Processing ${pending.length} families sequentially: ${pending.map(f => f.familyName).join(', ')}`)

    // ---------------------------------------------------------------------------
    // Sequential loop: one family fully completes (all phases + commit) before
    // the next starts. This is required because:
    //   - all families share tests/DevOpsMigrationPlatform.Testing
    //   - only one dotnet test run can execute at a time
    //   - git commits must be sequential
    // ---------------------------------------------------------------------------

    const PHASE_SCHEMA = {
      type: 'object',
      properties: {
        familyName: { type: 'string' },
        status: { type: 'string', enum: ['ok', 'blocked', 'failed'] },
        outputFile: { type: 'string' },
        summary: { type: 'string' },
      },
      required: ['familyName', 'status', 'outputFile', 'summary'],
    }

    const VERIFICATION_SCHEMA = {
      type: 'object',
      properties: {
        familyName: { type: 'string' },
        verdict: { type: 'string', enum: ['PASS', 'BLOCKED', 'FAIL'] },
        migratedScenarios: { type: 'array', items: { type: 'string' } },
        blockedScenarios: { type: 'array', items: { type: 'string' } },
        failedScenarios: { type: 'array', items: { type: 'string' } },
        wiringState: { type: 'string' },
        commitSha: { type: 'string' },
        commitMessage: { type: 'string' },
        summary: { type: 'string' },
      },
      required: ['familyName', 'verdict', 'migratedScenarios', 'blockedScenarios', 'failedScenarios', 'wiringState', 'summary'],
    }

    const results = []

    for (const file of pending) {
      log(`Starting family: ${file.familyName} (${file.featureFilePath})`)
      let blocked = false
      let blockReason = ''

      // --- Phase: Assessment ---
      phase('Assessment')
      const assessment = await agent(
        `Run the nkda-testdsl-feature-assessment skill for feature family "${file.familyName}".
Feature file: ${file.featureFilePath}

Follow the skill exactly as written in .agents/skills/nkda-testdsl-feature-assessment/SKILL.md.
Produce:
- .output/nkda-testdsl/${file.familyName}/00-scenario-test-inventory.md
- .output/nkda-testdsl/${file.familyName}/01-feature-assessment.md

Return structured output.`,
        { label: `assessment:${file.familyName}`, schema: PHASE_SCHEMA }
      )
      if (!assessment || assessment.status === 'failed') {
        blocked = true
        blockReason = assessment?.summary || 'Assessment failed'
      }

      // --- Phase: DSL Design ---
      if (!blocked) {
        phase('DSL Design')
        const dslDesign = await agent(
          `Run the nkda-testdsl-dsl-design skill for feature family "${file.familyName}".
Feature file: ${file.featureFilePath}

Consume .output/nkda-testdsl/${file.familyName}/01-feature-assessment.md.
Follow the skill exactly as written in .agents/skills/nkda-testdsl-dsl-design/SKILL.md.
Produce .output/nkda-testdsl/${file.familyName}/02-dsl-design.md.

Return structured output.`,
          { label: `dsl-design:${file.familyName}`, schema: PHASE_SCHEMA }
        )
        if (!dslDesign || dslDesign.status === 'failed') {
          blocked = true
          blockReason = dslDesign?.summary || 'DSL Design failed'
        }
      }

      // --- Phase: Extraction ---
      if (!blocked) {
        phase('Extraction')
        const extraction = await agent(
          `Run the nkda-testdsl-extraction skill for feature family "${file.familyName}".
Feature file: ${file.featureFilePath}

Consume .output/nkda-testdsl/${file.familyName}/02-dsl-design.md.
Follow the skill exactly as written in .agents/skills/nkda-testdsl-extraction/SKILL.md.

Bootstrap tests/DevOpsMigrationPlatform.Testing if it does not exist — this is never a blocker.
Purge orphaned generated Features/*.feature.cs files in the target test project before extraction.
Produce .output/nkda-testdsl/${file.familyName}/03-extraction-summary.md.

Return structured output.`,
          { label: `extraction:${file.familyName}`, schema: PHASE_SCHEMA }
        )
        if (!extraction || extraction.status === 'failed') {
          blocked = true
          blockReason = extraction?.summary || 'Extraction failed'
        }
      }

      // --- Phase: Conversion ---
      if (!blocked) {
        phase('Conversion')
        const conversion = await agent(
          `Run the nkda-testdsl-feature-conversion skill for feature family "${file.familyName}".
Feature file: ${file.featureFilePath}

Consume:
- .output/nkda-testdsl/${file.familyName}/01-feature-assessment.md
- .output/nkda-testdsl/${file.familyName}/02-dsl-design.md

Follow the skill exactly as written in .agents/skills/nkda-testdsl-feature-conversion/SKILL.md.

For each scenario:
- Build and run its mapped test.
- If the test passes: retire the scenario from the .feature file (remove that scenario block).
- If the test fails: retain the scenario in the .feature file.
- Every new or modified test method must carry [TestCategory("UnitTest")] immediately above [TestMethod].
- Check the existing test corpus before building any test; map to pre-existing, extend partial-existing, build only to-build.
- For missing-step scenarios with no pre-existing coverage, generate intent-derived tests.

Produce .output/nkda-testdsl/${file.familyName}/04-conversion-summary.md.

Return structured output.`,
          { label: `conversion:${file.familyName}`, schema: PHASE_SCHEMA }
        )
        if (!conversion || conversion.status === 'failed') {
          blocked = true
          blockReason = conversion?.summary || 'Conversion failed'
        }
      }

      // --- Phase: Refactor ---
      if (!blocked) {
        phase('Refactor')
        const refactor = await agent(
          `Run the nkda-testdsl-refactor skill for feature family "${file.familyName}".
Feature file: ${file.featureFilePath}

Follow the skill exactly as written in .agents/skills/nkda-testdsl-refactor/SKILL.md.
Produce .output/nkda-testdsl/${file.familyName}/05-refactor-summary.md.

Return structured output.`,
          { label: `refactor:${file.familyName}`, schema: PHASE_SCHEMA }
        )
        if (!refactor || refactor.status === 'failed') {
          blocked = true
          blockReason = refactor?.summary || 'Refactor failed'
        }
      }

      // --- Phase: Verification + Commit ---
      phase('Verification')
      let verificationResult
      if (blocked) {
        verificationResult = {
          familyName: file.familyName,
          verdict: 'BLOCKED',
          migratedScenarios: [],
          blockedScenarios: [],
          failedScenarios: [],
          wiringState: 'unknown',
          commitSha: '',
          commitMessage: '',
          summary: blockReason,
        }
      } else {
        verificationResult = await agent(
          `Run the nkda-testdsl-verification skill for feature family "${file.familyName}".
Feature file: ${file.featureFilePath}

Follow the skill exactly as written in .agents/skills/nkda-testdsl-verification/SKILL.md.

Required test execution order:
1. Run converted/affected feature-family tests first.
2. Verify every retired scenario has a mapped passing test with path:line evidence.
3. If all scenarios are retired and tests are green, run the full repository test suite.

If verification returns PASS:
- Delete the .feature file.
- Delete any generated .feature.cs and legacy *Steps.cs scoped to wiring state.
- Commit all changes with message: migrate: ${file.familyName} feature → DSL

If verification returns BLOCKED or FAIL:
- Retain the .feature file (with only unconverted scenarios remaining).
- Append every retained scenario as an entry in analysis/dsl-gaps-detected.md.
- Commit partial progress with message: migrate(partial): ${file.familyName} <N> scenarios retired

Produce .output/nkda-testdsl/${file.familyName}/06-verification.md.

Return structured output including the verdict, lists of migrated/blocked/failed scenarios, wiring state, and commit SHA.`,
          { label: `verification:${file.familyName}`, schema: VERIFICATION_SCHEMA }
        )
      }

      results.push(verificationResult)
      log(`Completed family: ${file.familyName} — ${verificationResult?.verdict ?? 'unknown'}`)
    }

    // ---------------------------------------------------------------------------
    // Terminal report
    // ---------------------------------------------------------------------------
    const verified = results.filter(Boolean)

    for (const r of verified) {
      const terminalStatus =
        r.verdict === 'PASS'
          ? (r.migratedScenarios?.length > 0 ? 'converted' : 'already-adapted')
          : r.verdict === 'BLOCKED'
          ? 'blocked'
          : 'failed'

      log(`
**\`${r.familyName}\` — \`${terminalStatus}\`**

| | Count |
|---|---|
| ✅ Migrated & committed | ${r.migratedScenarios?.length ?? 0} |
| 🚧 Blocked (gap) | ${r.blockedScenarios?.length ?? 0} |
| ⚠️ Failed | ${r.failedScenarios?.length ?? 0} |
| Total | ${(r.migratedScenarios?.length ?? 0) + (r.blockedScenarios?.length ?? 0) + (r.failedScenarios?.length ?? 0)} |

**Migrated:**
${(r.migratedScenarios ?? []).map(s => `- ${s} ✅`).join('\n') || '- (none)'}

**Blocked:**
${(r.blockedScenarios ?? []).map(s => `- ${s}`).join('\n') || '- (none)'}

**Failed:**
${(r.failedScenarios ?? []).map(s => `- ${s}`).join('\n') || '- (none)'}

**Wiring state:** \`${r.wiringState}\`
**Commit:** \`${r.commitSha || 'none'}\` — ${r.commitMessage || r.summary}
`)
    }

    return { processed: verified.length, results: verified }
  }
}
