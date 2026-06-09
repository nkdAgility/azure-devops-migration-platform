export const meta = {
  name: 'nkda-testcategory-workflow',
  description: 'Apply canonical TestCategory tags to every [TestMethod] in every test .cs file, one file at a time per project',
  phases: [
    { title: 'Discover', detail: 'Find all .cs files containing [TestMethod] per project' },
    { title: 'Remediate', detail: 'Read, classify, and write correct TestCategory tags per file' },
  ],
}

// ---------------------------------------------------------------------------
// Rules injected into every remediation agent
// ---------------------------------------------------------------------------
const RULES = `
## TestCategory Tagging Rules — MANDATORY, NO EXCEPTIONS

### STEP 0 — CLASS-LEVEL PURGE (do this FIRST, before touching any method):
Scan the entire file for [TestCategory(...)] attributes. Any [TestCategory] that appears
on the same line as [TestClass], or on the line(s) immediately above [TestClass], MUST be
deleted. No exceptions. [TestCategory] is NEVER valid on a [TestClass].

### Two tags required on every [TestMethod]:
1. Parent family:  [TestCategory("CodeTest")]  OR  [TestCategory("SystemTest")]
2. Specific:       one canonical value from the list below

Both tags are MANDATORY. A method with only one tag is non-compliant.

### Canonical specific values:
- UnitTests        — single class in isolation, ALL deps mocked via Moq or hand-written fakes,
                     no real infrastructure, no I/O. Constructor/new + mock setup only.
- DomainTests      — ONLY if the test directly calls the DevOpsMigrationPlatform.Testing DSL:
                     builders like A.WorkItem(), typed runners, DSL assertion helpers.
                     If there is NO import of DevOpsMigrationPlatform.Testing and NO DSL builder
                     calls, this category MUST NOT be used — use UnitTests or IntegrationTests.
- IntegrationTests — real infrastructure components exercised in-process with no external network:
                     real Polly retry/backoff policy, real HttpClient, real Task.Delay as a wait,
                     real channels/drain loops, real serialisers, real ActivitySource listeners,
                     real ILogger capture, real MemoryStream pipelines.
                     If the test spins up real library behaviour (not just mocks), use this.
- SystemTest_Smoke      — OPERATOR-APPLIED ONLY. Never assign this tag automatically.
                          Only a human operator may designate a test as smoke. If a test
                          currently carries [TestCategory("SystemTest_Smoke")] leave it alone.
                          Do not add it to any test that does not already have it.
- SystemTest_Simulated  — end-to-end flow that uses the Simulated connector specifically
                          (i.e. the connector type is "Simulated" / SimulatedConnector).
                          Do NOT use this for tests that merely avoid external network calls —
                          "no network" alone does not qualify. The test must exercise a full
                          pipeline path through the Simulated connector implementation.
- SystemTest_Live       — requires live ADO/TFS credentials

### Parent family mapping:
- UnitTests, DomainTests, IntegrationTests  →  [TestCategory("CodeTest")]
- SystemTest_Smoke, SystemTest_Simulated, SystemTest_Live  →  [TestCategory("SystemTest")]

### Classification decision order (first match wins):
1. File imports DevOpsMigrationPlatform.Testing AND test calls DSL builders/runners  →  DomainTests
2. Requires live ADO/TFS (real org URLs, credential env vars)  →  SystemTest_Live
3. Uses the Simulated connector specifically (SimulatedConnector / connector type "Simulated")
   in an end-to-end pipeline flow  →  SystemTest_Simulated
4. Already tagged SystemTest_Smoke by an operator  →  leave as-is, NEVER add this tag
5. Uses real library/framework infrastructure in-process (real Polly, real HttpClient,
   real Task.Delay, real channels, real ActivitySource, real ILogger capture)  →  IntegrationTests
6. Single class, all deps mocked via Moq or fakes, no real infrastructure  →  UnitTests
7. AMBIGUOUS between two adjacent categories  →  go one level UP (e.g. UnitTests → IntegrationTests)

### SetupSequence retry signal — CRITICAL:
If a test uses Moq's SetupSequence() with N > 1 identical or near-identical returns on the
same method, that is NOT a UnitTest — it means the SUT calls that dependency multiple times,
which only happens when a real retry policy (e.g. real Polly) is driving the loop.
A pure UnitTest calls each dependency once and uses Setup(), not SetupSequence().
SetupSequence with 2+ returns on the same mock member → IntegrationTests.

### DomainTests boundary — CRITICAL:
DomainTests requires EXPLICIT DSL usage. The presence of domain objects alone is NOT sufficient.
"Uses Moq" + "arranges domain state" = UnitTests or IntegrationTests, NOT DomainTests.
If you are unsure whether DSL is used, look for: using DevOpsMigrationPlatform.Testing; and calls
to A.<Something>(), typed builder chains, or DSL runner/assertion types. If absent → not DomainTests.

### Placement rule:
The two [TestCategory] attributes must appear IMMEDIATELY above [TestMethod], in this order:
  [TestCategory("CodeTest")]        ← parent first
  [TestCategory("UnitTests")]       ← specific second
  [TestMethod]
  public async Task ...

### Non-canonical tags — DELETE on contact (do not preserve, do not move):
"UnitTest" (singular), "DomainTest" (singular), "IntegrationTest" (singular),
"SystemTest" alone, "filter", "offline", "simulated" (lowercase), "Local", "Cleanup",
"NET481", "cli", "cli-execute", "cli-architecture", "auth-flow", "config-flow",
"telemetry-flow", "di-registration", "di-isolation", "options-validation", "help-text",
"missing-params", "error-case", "discovery-inventory", "custom-config", "default-config",
"tfs-object-model", and any other string not in the canonical list above.
`

// ---------------------------------------------------------------------------
// Projects to process (Testing.Dsl is infrastructure, not tests — excluded)
// ---------------------------------------------------------------------------
const PROJECTS = [
  'tests/DevOpsMigrationPlatform.Abstractions.Tests',
  'tests/DevOpsMigrationPlatform.CLI.Migration.Tests',
  'tests/DevOpsMigrationPlatform.ControlPlane.Tests',
  'tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests',
  'tests/DevOpsMigrationPlatform.Infrastructure.ControlPlane.Tests',
  'tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests',
  'tests/DevOpsMigrationPlatform.Infrastructure.Tests',
  'tests/DevOpsMigrationPlatform.MigrationAgent.Tests',
  'tests/DevOpsMigrationPlatform.SchemaGenerator.Tests',
  'tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests',
]

const REPO = 'C:\\Users\\MartinHinshelwoodNKD\\source\\repos\\azure-devops-migration-platform'

// ---------------------------------------------------------------------------
// Schemas
// ---------------------------------------------------------------------------
const FILE_LIST_SCHEMA = {
  type: 'object',
  properties: {
    files: {
      type: 'array',
      items: { type: 'string' },
      description: 'Absolute Windows paths to .cs files that contain at least one [TestMethod] attribute'
    }
  },
  required: ['files']
}

const REMEDIATION_SCHEMA = {
  type: 'object',
  properties: {
    file: { type: 'string', description: 'Absolute path of the file processed' },
    changed: { type: 'boolean', description: 'Whether any changes were made' },
    summary: { type: 'string', description: 'Brief description of changes made, or "no changes needed"' }
  },
  required: ['file', 'changed', 'summary']
}

const COMMIT_SCHEMA = {
  type: 'object',
  properties: {
    committed: { type: 'boolean' },
    message: { type: 'string' }
  },
  required: ['committed', 'message']
}

// ---------------------------------------------------------------------------
// Phase 1: Discover — find test files per project in parallel
// ---------------------------------------------------------------------------
phase('Discover')

const projectFileLists = await parallel(PROJECTS.map(proj => () =>
  agent(
    `Find all .cs files (excluding obj/ directories) inside:
${REPO}\\${proj.replace(/\//g, '\\\\')}

Return only files that contain at least one line matching [TestMethod].
Return their absolute Windows paths.`,
    {
      label: `discover:${proj.split('/').pop()}`,
      phase: 'Discover',
      schema: FILE_LIST_SCHEMA
    }
  )
))

const allFiles = projectFileLists
  .filter(Boolean)
  .flatMap(r => r.files)

log(`Discovered ${allFiles.length} test files across ${PROJECTS.length} projects`)

// ---------------------------------------------------------------------------
// Phase 2: Remediate — one file at a time through the pipeline
// ---------------------------------------------------------------------------
phase('Remediate')

const results = await pipeline(
  allFiles,
  (filePath) => agent(
    `You are applying canonical TestCategory attributes to a C# MSTest file. Follow the rules below exactly — no other changes.

${RULES}

## File to process
${filePath}

## Instructions
1. Read the file in full.
2. FIRST — class-level purge: find every [TestClass] declaration. Delete ALL [TestCategory(...)]
   attributes that appear on the same line or on any line immediately above [TestClass]. This is
   a hard requirement — do it before touching any [TestMethod].
3. Check the using directives. If there is NO "using DevOpsMigrationPlatform.Testing" import,
   DomainTests is NOT a valid category for any method in this file.
4. For every [TestMethod] in the file:
   a. Read the method body. Classify using the decision order in the rules above.
      - If calling DevOpsMigrationPlatform.Testing DSL builders/runners → DomainTests
      - If using real Polly, real HttpClient, real Task.Delay, real channels, real ActivitySource,
        real ILogger capture, real MemoryStream pipelines → IntegrationTests
      - If using Moq SetupSequence() with 2 or more returns on the same mock member → IntegrationTests
        (the extra returns only exist because a real retry policy drives multiple calls)
      - If using Moq Setup() (single call per dependency) and no real infrastructure → UnitTests
   b. Remove ALL existing [TestCategory(...)] attributes immediately above that [TestMethod]
      (including non-canonical ones like "UnitTest", "IntegrationTest", "cli-execute", etc.).
   c. Insert exactly two [TestCategory] lines immediately above [TestMethod]:
        [TestCategory("CodeTest")]      ← or SystemTest for system tests
        [TestCategory("UnitTests")]     ← the specific canonical value
        [TestMethod]
5. Do not change anything else — no whitespace reformatting, no logic, no comments, no reordering.
6. Write the corrected file using the Edit or Write tool.

Return a summary of what changed, listing each method and the tags applied.`,
    {
      label: `remediate:${filePath.split('\\').pop()}`,
      phase: 'Remediate',
      schema: REMEDIATION_SCHEMA
    }
  ),
  async (result) => {
    if (!result || !result.changed) return result
    const filePath = result.file
    const fileName = filePath.split('\\').pop()
    await agent(
      `Run the following git command exactly in the repository at C:\\Users\\MartinHinshelwoodNKD\\source\\repos\\azure-devops-migration-platform:

git add "${filePath}"
git commit -m "fix(tests): apply canonical TestCategory tags to ${fileName}

${result.summary}

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"

Use the Bash tool to run these commands. Report whether the commit succeeded.`,
      {
        label: `commit:${fileName}`,
        phase: 'Remediate',
        schema: COMMIT_SCHEMA
      }
    )
    return result
  }
)

const changed = results.filter(Boolean).filter(r => r.changed)
const unchanged = results.filter(Boolean).filter(r => !r.changed)

log(`Remediation complete: ${changed.length} files updated, ${unchanged.length} already compliant`)
