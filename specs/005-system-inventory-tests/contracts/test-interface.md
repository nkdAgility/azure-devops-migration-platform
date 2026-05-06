# Test Interface Contract

## SystemTest Infrastructure Interface

This contract defines how system tests integrate with the existing test infrastructure:

### Test Method Signature
```csharp
[TestMethod]
[TestCategory("SystemTest")]
public async Task <TestName>_SystemTest_<Scenario>()
{
    // Implementation follows established patterns
}
```

### Environment Variable Contract
```bash
# Required environment variables for system test execution
AZDEVOPS_SYSTEM_TEST_ORG=<organization-name>
AZDEVOPS_SYSTEM_TEST_PAT=<personal-access-token>

# Optional environment variables for advanced scenarios  
AZDEVOPS_SYSTEM_TEST_TIMEOUT=<timeout-seconds>  # Default: 30
AZDEVOPS_SYSTEM_TEST_RETRY_COUNT=<retry-count>   # Default: 3
```

### Test Execution Contract
```bash
# Run all system tests
dotnet test --filter "TestCategory=SystemTest"

# Run system tests with verbose output
dotnet test --filter "TestCategory=SystemTest" --logger "console;verbosity=detailed"

# Exclude system tests from regular runs
dotnet test --filter "TestCategory!=SystemTest"
```

### Test Outcome Contract
- **Success (Exit 0)**: System test passed, inventory command functionality validated
- **Inconclusive**: Environment not configured, test skipped with clear instructions
- **Failure (Exit 1)**: Actual test failure, indicates bug in inventory command or infrastructure

### Error Message Contract
```csharp
// Environment configuration errors
"System test skipped: Environment variables not configured. " +
"Set AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT to run this test. " +
"See docs/contributor-guide.md for setup instructions."

// Authentication errors
"Authentication failed for organization '{org}'. " +
"Verify AZDEVOPS_SYSTEM_TEST_PAT token has required permissions. " +
"See docs/contributor-guide.md troubleshooting section."

// Connectivity errors
"Cannot connect to Azure DevOps organization '{org}': {error}. " +
"Verify network connectivity and organization accessibility."
```