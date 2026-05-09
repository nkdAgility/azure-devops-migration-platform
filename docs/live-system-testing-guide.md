# Live System Testing Guide

Audience: Contributors.

Use this guide when a behaviour must be proven against a real Azure DevOps or TFS environment and lower test layers are insufficient.

## When To Use Live Tests

Live system tests are the last layer in the hierarchy.

Use them only when you need to prove one of these:

- a real Azure DevOps or TFS connector behaviour
- a real authentication or permission boundary
- a real service-side contract that Simulated cannot model credibly

Do not add a live test for logic that can be proven with unit, feature, or simulated system coverage.

## Repository Rules

- Tag live Azure DevOps tests with `[TestCategory("SystemTest")]`.
- Some suites may also use `[TestCategory("SystemTest_Live")]` as an additional marker.
- Do not commit `[Ignore]` or `Assert.Inconclusive()` as environment gating.
- Gate live tests at the runner or workflow level instead of self-skipping inside the test body.

## Local Setup

Azure DevOps live tests require environment variables such as:

| Variable | Purpose | Example |
| --- | --- | --- |
| `AZDEVOPS_SYSTEM_TEST_ORG` | Azure DevOps organisation name | `contoso` |
| `AZDEVOPS_SYSTEM_TEST_PAT` | Personal Access Token | `$ENV:AZDEVOPS_SYSTEM_TEST_PAT` |

### PowerShell

```powershell
$env:AZDEVOPS_SYSTEM_TEST_ORG = "your-org-name"
$env:AZDEVOPS_SYSTEM_TEST_PAT = "your-pat-token-here"
dotnet test --filter "TestCategory=SystemTest" --logger "console;verbosity=detailed"
```

### Command Prompt

```cmd
set AZDEVOPS_SYSTEM_TEST_ORG=your-org-name
set AZDEVOPS_SYSTEM_TEST_PAT=your-pat-token-here
dotnet test --filter "TestCategory=SystemTest" --logger "console;verbosity=detailed"
```

### Linux Or macOS

```bash
export AZDEVOPS_SYSTEM_TEST_ORG="your-org-name"
export AZDEVOPS_SYSTEM_TEST_PAT="your-pat-token-here"
dotnet test --filter "TestCategory=SystemTest" --logger "console;verbosity=detailed"
```

## Token Guidance

- Never commit tokens to source control.
- Use environment variables or the repository’s supported `$ENV:VARIABLE_NAME` config resolution.
- Prefer minimum required scopes.
- Prefer shorter-lived tokens and rotation.

For Azure DevOps read-oriented test coverage, typical minimum scopes include:

- Project and Team: Read
- Work Items: Read
- Build: Read when build-related behaviour is under test
- Release: Read when release-related behaviour is under test

## CI And Workflow Gating

Live tests should be turned on by workflow conditions and environment availability, not by committed self-skipping test bodies.

Example GitHub Actions shape:

```yaml
name: System Tests
on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  system-tests:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore

    - name: Run System Tests
      run: dotnet test --filter "TestCategory=SystemTest" --logger "console;verbosity=detailed"
      env:
        AZDEVOPS_SYSTEM_TEST_ORG: ${{ secrets.AZDEVOPS_SYSTEM_TEST_ORG }}
        AZDEVOPS_SYSTEM_TEST_PAT: ${{ secrets.AZDEVOPS_SYSTEM_TEST_PAT }}
```

## Authoring Live Tests

When adding a new live test:

1. Prove first that lower layers cannot cover the behaviour adequately.
2. Tag it with the correct live category.
3. Assert observable output, not just absence of exceptions.
4. Keep credentials outside the test body.
5. Clean up any created resources when the behaviour is not naturally idempotent.

Avoid this pattern in committed code:

- checking environment variables inside the test and calling `Assert.Inconclusive()`
- marking tests with `[Ignore]` to simulate environment gating

Instead, run or exclude the live category deliberately from the command line or workflow.

## Troubleshooting Live Tests

If a live test fails:

1. Confirm the environment variables are present.
2. Confirm the token still has the required scopes and has not expired.
3. Confirm the test identity can access the organisation or collection under test.
4. Check network restrictions, proxy rules, or firewall issues.
5. Inspect the repository test diagnostics under `.output/workingtests/{TestMethodName}/.otel-diagnostics/`.

If you need the exact control-plane payloads, reproduce with diagnostics enabled and inspect the `inbox/` subfolder described in [testing-guide.md](testing-guide.md).

## Related Documents

- [testing-guide.md](testing-guide.md)
- [contributor-guide.md](contributor-guide.md)
- [security-and-data-sovereignty.md](security-and-data-sovereignty.md)
- [connector-development-guide.md](connector-development-guide.md)
