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

### Two tags required on every [TestMethod]:
1. Parent family:  [TestCategory("CodeTest")]  OR  [TestCategory("SystemTest")]
2. Specific:       one canonical value from the list below

### Canonical specific values:
- UnitTests        — single class in isolation, ALL deps mocked via Moq or hand-written fakes, no real infrastructure, no I/O
- DomainTests      — uses DevOpsMigrationPlatform.Testing DSL (builders like A.WorkItem(), runners, assertions)
- IntegrationTests — real infrastructure in-process: real Polly retry policy, real HttpClient, real Task.Delay as a timing mechanism, real channels/drain loops, real serialisers. No external network.
- SystemTest_Smoke      — critical-path subset run on every PR; requires full system active
- SystemTest_Simulated  — end-to-end with the Simulated connector; no network
- SystemTest_Live       — requires live ADO/TFS credentials

### Parent family mapping:
- UnitTests, DomainTests, IntegrationTests  →  [TestCategory("CodeTest")]
- SystemTest_Smoke, SystemTest_Simulated, SystemTest_Live  →  [TestCategory("SystemTest")]

### Classification decision order (first match wins):
1. Uses DevOpsMigrationPlatform.Testing DSL (A.WorkItem(), builders, runners, DSL assertions)  →  DomainTests
2. Requires live ADO/TFS (real org URLs, credential env vars)  →  SystemTest_Live
3. Uses Simulated connector end-to-end  →  SystemTest_Simulated
4. Critical-path smoke subset  →  SystemTest_Smoke
5. Real infrastructure in-process (real Polly, real HttpClient, real Task.Delay waits, real drain loops, real filesystem)  →  IntegrationTests
6. Single class, all deps mocked, no real infrastructure  →  UnitTests
7. AMBIGUOUS between two adjacent categories  →  go one level UP

### Class-level tags:
- NEVER put [TestCategory] on [TestClass] — remove any that exist

### Non-canonical tags — correct on contact:
The following are non-canonical and must be replaced with the correct canonical pair:
"UnitTest" (singular), "UnitTests" without parent, "DomainTest" (singular), "IntegrationTest" (singular),
"SystemTest" alone on a CodeTest, "filter", "offline", "simulated" (lowercase), "Local", "Cleanup",
"NET481", "cli", "cli-execute", "cli-architecture", "auth-flow", "config-flow", "telemetry-flow",
"di-registration", "di-isolation", "options-validation", "help-text", "missing-params",
"error-case", "discovery-inventory", "custom-config", "default-config", "tfs-object-model",
and any other non-canonical string.
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
  .slice(0, 20)

log(`Discovered ${allFiles.length} test files (capped at 20 for pilot run)`)

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
1. Read the file.
2. Remove any [TestCategory(...)] attribute that sits on a [TestClass] line or immediately above one.
3. For every [TestMethod] in the file:
   a. Identify the correct parent tag (CodeTest or SystemTest) and specific tag (UnitTests, DomainTests, IntegrationTests, SystemTest_Smoke, SystemTest_Simulated, SystemTest_Live) using the classification rules above.
   b. Remove any existing [TestCategory(...)] attributes immediately above that [TestMethod].
   c. Add the two correct [TestCategory] attributes immediately above [TestMethod], parent tag first.
4. Do not change anything else — no whitespace reformatting, no logic, no comments, no using statements, no reordering.
5. Write the corrected file using the Edit or Write tool.

Return a summary of what changed.`,
    {
      label: `remediate:${filePath.split('\\').pop()}`,
      phase: 'Remediate',
      schema: REMEDIATION_SCHEMA
    }
  )
)

const changed = results.filter(Boolean).filter(r => r.changed)
const unchanged = results.filter(Boolean).filter(r => !r.changed)

log(`Remediation complete: ${changed.length} files updated, ${unchanged.length} already compliant`)
