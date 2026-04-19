# Quick Start: System Test Framework for Inventory Command

## 30-Second Setup (Local Development)

### Prerequisites
- .NET 10 SDK installed
- Access to an Azure DevOps organization
- Personal Access Token with Project/Team read permissions

### Environment Setup
```bash
# Windows (PowerShell)
$env:AZDEVOPS_SYSTEM_TEST_ORG = "your-org-name"
$env:AZDEVOPS_SYSTEM_TEST_PAT = "your-pat-token"

# Linux/macOS (Bash)
export AZDEVOPS_SYSTEM_TEST_ORG="your-org-name"  
export AZDEVOPS_SYSTEM_TEST_PAT="your-pat-token"
```

### Run System Tests
```bash
# Execute system tests only
dotnet test --filter "TestCategory=SystemTest"

# Exclude system tests from regular runs  
dotnet test --filter "TestCategory!=SystemTest"
```

## Verification Commands

### Test Environment Configuration
```bash
# Verify environment variables are set
echo $AZDEVOPS_SYSTEM_TEST_ORG    # Should show your organization name
echo "PAT configured: $(if [ -n "$AZDEVOPS_SYSTEM_TEST_PAT" ]; then echo "Yes"; else echo "No"; fi)"
```

### Test Token Validity  
```bash
# Quick connectivity check using curl
curl -H "Authorization: Basic $(echo -n ":$AZDEVOPS_SYSTEM_TEST_PAT" | base64)" \
     "https://dev.azure.com/$AZDEVOPS_SYSTEM_TEST_ORG/_apis/projects?api-version=6.0"
```

## What System Tests Validate

- ✅ Inventory command configuration parsing
- ✅ Azure DevOps authentication with PAT tokens
- ✅ Token resolution via existing `TokenResolver.Resolve()` pattern
- ✅ Dependency injection setup for inventory command
- ✅ Option validation logic with live configuration
- ✅ Error handling for invalid credentials or unreachable services

## Expected Outcomes

### Successful Run
```text
✅ System test passed for organization: your-org-name
✅ Token resolution successful  
✅ Options validation passed
📁 Test output directory: /tmp/inventory-test-{guid}
```

### Environment Not Configured
```text
Test Outcome: Inconclusive
Message: System test skipped: Environment variables not configured.
Set AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT to run this test.
See docs/contributors.md for setup instructions.
```

### Authentication Failed
```text
Test Outcome: Failed
Message: Authentication failed for organization 'your-org-name'.
Verify AZDEVOPS_SYSTEM_TEST_PAT token has required permissions.
```

## Next Steps

- **Add New Tests**: Follow patterns in `InventoryCommandTests.cs` with `[TestCategory("SystemTest")]`
- **CI Integration**: Configure repository secrets `AZDEVOPS_SYSTEM_TEST_ORG` and `AZDEVOPS_SYSTEM_TEST_PAT` 
- **Troubleshooting**: See comprehensive guide in `docs/contributors.md`
- **Security**: Never commit tokens to source control, always use environment variables

## Test Categories

```bash
# Available test filtering options
dotnet test --filter "TestCategory=Unit"           # Fast unit tests only
dotnet test --filter "TestCategory=Integration"    # Multi-component integration  
dotnet test --filter "TestCategory=SystemTest"     # Live external system dependencies
dotnet test --filter "TestCategory!=SystemTest"    # Everything except system tests
```

## Development Workflow

1. **Write Code**: Implement inventory command changes
2. **Unit Tests**: `dotnet test --filter "TestCategory=Unit"` (fast feedback)
3. **Integration Tests**: `dotnet test --filter "TestCategory=Integration"` (component interaction)
4. **System Tests**: `dotnet test --filter "TestCategory=SystemTest"` (live validation)
5. **Commit**: All test categories passing before code submission

## Time Investment

- **Setup**: ~2 minutes (one-time environment configuration)
- **Unit Tests**: ~10 seconds (immediate feedback loop)
- **Integration Tests**: ~30 seconds (component validation)
- **System Tests**: ~30 seconds (live system validation)
- **Full Suite**: ~1 minute (complete confidence before commit)