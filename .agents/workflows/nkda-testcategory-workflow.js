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
## TestCategory Classification — Reasoning Guide

Your job is to classify each [TestMethod] by understanding what the test is actually doing,
not by mechanically matching keywords. Read the whole method — imports, arrange, act, assert —
and ask: "What is this test exercising, and what does it depend on to do so?"

---

### The two mandatory tags

Every [TestMethod] needs exactly two [TestCategory] attributes, placed immediately above it:

  [TestCategory("CodeTest")]     ← or "SystemTest"
  [TestCategory("UnitTests")]    ← or one of the specific values below
  [TestMethod]

Parent "CodeTest" means: the test runs entirely in-process, no live system needed.
Parent "SystemTest" means: the test requires the full system to be active.

---

### What each specific category means (reason about intent, not just surface features)

**UnitTests**
The test exercises a single class in complete isolation. Every collaborator is replaced with
a Moq mock or a hand-written fake. The real question to ask: "Could this test pass without
any real library or framework code running?" If yes → UnitTests.
Signals: Mock<T>.Setup() called once per dependency. No real timers, no real HTTP, no real
channels, no real retry loops. The SUT is constructed directly with fakes.
Counter-signal: if the mock is set up via SetupSequence() returning the same value multiple
times, ask WHY — a unit test only calls each dependency once. Multiple calls mean something
real is driving a loop (retry, polling, drain) → that is NOT UnitTests.

**DomainTests**
The test uses the DevOpsMigrationPlatform.Testing internal DSL to express business behaviour.
Look for: "using DevOpsMigrationPlatform.Testing" in the imports, and calls to typed builders
(A.WorkItem(), A.Migration(), etc.), DSL runners, or DSL assertion helpers.
The presence of domain objects or value types alone does NOT make a test a DomainTest.
If there is no DSL import and no builder/runner calls → this category must not be used.

**IntegrationTests**
The test exercises real library or framework components wired together in-process.
"Real" means the actual implementation runs — not a mock, not a fake.
Ask: "Is anything other than the test's own code actually executing?"
Examples of real infrastructure: Polly retry/backoff policies, HttpClient pipelines,
System.Threading.Channels drain loops, ActivitySource/ActivityListener, ILogger implementations
(including hand-written capturing loggers like CapturingStoreLogger), JSON serialisers,
MemoryStream pipelines, real Task.Delay as a timing mechanism.
A hand-written fake logger that captures entries IS real infrastructure — it implements a real
interface with real behaviour the test depends on.
A common pattern: the mock needs SetupSequence() with N>1 returns because a real retry policy
calls the dependency multiple times. The mock is still a mock, but the retry loop is real →
IntegrationTests.

**SystemTest_Simulated**
An end-to-end test that runs through the Simulated connector specifically. The connector type
must be "Simulated" / SimulatedConnector. "No external network" alone is not enough — the test
must exercise a full pipeline path through the Simulated connector implementation.

**SystemTest_Live**
Requires live ADO/TFS credentials or real external endpoints. Usually guards on env vars and
calls Assert.Fail if they are absent.

**SystemTest_Smoke**
OPERATOR-APPLIED ONLY. Never add this tag. If a test already has it, leave it exactly as-is.

---

### When in doubt, go one level up

If a test sits ambiguously between UnitTests and IntegrationTests, classify it as IntegrationTests.
If it sits ambiguously between IntegrationTests and a SystemTest, classify it as IntegrationTests.
Never downgrade a test to make it "simpler" — upgrade it to reflect what it actually exercises.

---

### Class-level [TestCategory] — always wrong, always remove

[TestCategory] must never appear on [TestClass]. Scan the file first and delete any
[TestCategory(...)] attribute that sits on or immediately above a [TestClass] declaration.

---

### Non-canonical tag values — delete, do not preserve

Only these strings are valid: CodeTest, SystemTest, UnitTests, DomainTests, IntegrationTests,
SystemTest_Smoke, SystemTest_Simulated, SystemTest_Live.
Any other string (e.g. "UnitTest", "IntegrationTest", "cli-execute", "auth-flow", "offline",
"filter", "Local", "NET481", or any descriptive label) must be deleted, not moved or renamed.
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
1. Read the entire file — imports, class body, every method.
2. Remove any [TestCategory(...)] on or above [TestClass]. There should be none; delete any you find.
3. For every [TestMethod], reason about what the test is actually doing:
   - What does it import? Does it use the DevOpsMigrationPlatform.Testing DSL?
   - What real code runs when this test executes — not just what is mocked, but what is NOT mocked?
   - If a mock uses SetupSequence() with multiple returns of the same value, ask why the SUT
     calls that dependency more than once. A retry loop is the usual answer → IntegrationTests.
   - A hand-written logger that captures entries (e.g. CapturingStoreLogger) is real infrastructure.
   - Use the reasoning guide above to reach a classification. When uncertain, go one level up.
4. Remove ALL existing [TestCategory(...)] attributes above each [TestMethod] — canonical or not.
5. Insert exactly two [TestCategory] lines immediately above [TestMethod], parent tag first:
     [TestCategory("CodeTest")]
     [TestCategory("UnitTests")]
     [TestMethod]
6. Do not change anything else — no formatting, no logic, no comments, no reordering.
7. Write the corrected file.

Return a summary listing each method and the classification reasoning that led to its tags.`,
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
