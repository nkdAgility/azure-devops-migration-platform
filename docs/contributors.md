# Contributors Guide

This guide provides information for developers contributing to the Azure DevOps Migration Platform.

## Testing

The project uses a comprehensive testing strategy with multiple types of tests:

### Test Categories

- **Unit Tests**: Fast, isolated tests that test individual components with mocked dependencies
- **Integration Tests**: Tests that verify multiple components working together
- **System Tests**: Tests that connect to live external systems (tagged with `SystemTest`)

### Running Tests

#### All Tests
```bash
dotnet test
```

#### Unit Tests Only (exclude system tests)
```bash
dotnet test --filter "TestCategory!=SystemTest"
```

#### System Tests Only
```bash
dotnet test --filter "TestCategory=SystemTest"
```

#### Specific Test Project
```bash
dotnet test tests/DevOpsMigrationPlatform.CLI.Migration.Tests
```

### System Tests

System tests are designed to validate functionality against live external systems. These tests require additional setup and configuration.

#### Azure DevOps System Tests

System tests for Azure DevOps functionality require the following environment variables:

| Variable | Purpose | Example |
|----------|---------|---------|
| `AZDEVOPS_SYSTEM_TEST_ORG` | Azure DevOps organization name | `contoso` |
| `AZDEVOPS_SYSTEM_TEST_PAT` | Personal Access Token | `$ENV:AZDEVOPS_SYSTEM_TEST_PAT` |

##### Setting Up Environment Variables

**For Local Development:**

1. Create an access token (Personal Access Token) in Azure DevOps:
   - Go to Azure DevOps → User Settings → Personal Access Tokens
   - Create a new token with the following scopes:
     - **Project and Team**: Read
     - **Work Items**: Read
     - **Build**: Read (if testing build-related features)
     - **Release**: Read (if testing release-related features)

2. Set environment variables:

   **Windows (PowerShell):**
   ```powershell
   $env:AZDEVOPS_SYSTEM_TEST_ORG = "your-org-name"
   $env:AZDEVOPS_SYSTEM_TEST_PAT = "your-pat-token-here"
   ```

   **Windows (Command Prompt):**
   ```cmd
   set AZDEVOPS_SYSTEM_TEST_ORG=your-org-name
   set AZDEVOPS_SYSTEM_TEST_PAT=your-pat-token-here
   ```

   **Linux/macOS:**
   ```bash
   export AZDEVOPS_SYSTEM_TEST_ORG="your-org-name"
   export AZDEVOPS_SYSTEM_TEST_PAT="your-pat-token-here"
   ```

3. Run system tests:
   ```bash
   dotnet test --filter "TestCategory=SystemTest" --logger "console;verbosity=detailed"
   ```

**For GitHub Actions:**

Add the following secrets to your GitHub repository:

- `AZDEVOPS_SYSTEM_TEST_ORG`: Your Azure DevOps organization name
- `AZDEVOPS_SYSTEM_TEST_PAT`: Your Personal Access Token

Example GitHub Actions workflow:

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

##### Token Security

- **Never commit tokens to source control**
- Use environment variables for all sensitive data
- The system supports the `$ENV:VARIABLE_NAME` syntax for token resolution
- Consider using shorter-lived tokens and rotating them regularly
- Use minimal scopes required for testing

##### Troubleshooting System Tests

If system tests fail:

1. **Check Environment Variables:**
   ```bash
   # Verify variables are set (don't echo the access token value)
   echo "Org: $AZDEVOPS_SYSTEM_TEST_ORG"
   echo "Access token set: $(if [ -n "$AZDEVOPS_SYSTEM_TEST_PAT" ]; then echo "Yes"; else echo "No"; fi)"
   ```

2. **Verify Token Permissions:**
   - Ensure the access token has the required scopes
   - Test the token manually with Azure DevOps REST API
   - Check if the token has expired

3. **Check Organization Access:**
   - Verify the organization name is correct
   - Ensure the account associated with the access token has access to the organization

4. **Network Connectivity:**
   - Verify internet connectivity
   - Check if corporate firewalls might be blocking Azure DevOps
   - Test with a simple curl command:
     ```bash
     curl -H "Authorization: Basic $(echo -n ":$AZDEVOPS_SYSTEM_TEST_PAT" | base64)" \
          "https://dev.azure.com/$AZDEVOPS_SYSTEM_TEST_ORG/_apis/projects"
     ```

### Test Organization

The tests follow this structure:

```
tests/
├─ DevOpsMigrationPlatform.CLI.Migration.Tests/
│  ├─ Commands/
│  │  ├─ Discovery/
│  │  │  └─ InventoryCommandTests.cs    # Contains system tests
│  │  └─ ...
│  └─ TestUtilities/
└─ ...
```

### Creating New System Tests

When creating new system tests:

1. Tag with `[TestCategory("SystemTest")]`
2. Use environment variables for configuration
3. Provide clear error messages when environment is not configured
4. Use `Assert.Inconclusive()` to skip tests when environment variables are missing
5. Clean up any resources created during testing
6. Include comprehensive documentation in the test comments

Example pattern:

```csharp
[TestMethod]
[TestCategory("SystemTest")]
public async Task MyCommand_SystemTest_CanConnectToLiveSystem()
{
    // Arrange - Check environment variables
    var requiredVar = Environment.GetEnvironmentVariable("REQUIRED_VAR");
    if (string.IsNullOrEmpty(requiredVar))
    {
        Assert.Inconclusive("System test skipped: REQUIRED_VAR not set");
        return;
    }

    // Test implementation...
}
```

## Code Style

[Additional code style guidelines would go here]

## Contributing Workflow

[Contribution workflow information would go here]

## Architecture Guidelines

For architectural information, see:
- [Architecture Overview](architecture.md)
- [System Architecture](../.agents/guardrails/system-architecture.md)
- [Coding Standards](../.agents/guardrails/coding-standards.md)

## Getting Started

[Getting started information would go here]