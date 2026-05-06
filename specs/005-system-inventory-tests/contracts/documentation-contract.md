# Documentation Contract

## Contributors Guide Requirements

The `docs/contributor-guide.md` documentation must provide comprehensive guidance for system test usage:

### Required Sections

#### 1. System Test Overview
- Definition and purpose of system tests vs unit/integration tests
- When to run system tests (local development, CI/CD)
- Test categorization and filtering commands

#### 2. Local Development Setup
- Step-by-step environment variable configuration
- Azure DevOps organization requirements and setup
- Personal Access Token creation with minimal required permissions
- Verification commands to test setup

#### 3. CI/CD Integration
- GitHub repository secrets configuration
- GitHub Actions workflow integration examples
- Security considerations and credential handling
- Conditional execution patterns

#### 4. Troubleshooting Guide
- Common error scenarios and solutions
- Environment variable validation steps
- Network connectivity troubleshooting
- Azure DevOps permissions verification
- Token expiration and renewal guidance

#### 5. Test Development Guidelines
- How to add new system tests
- Test naming conventions and categorization
- Error handling patterns
- Resource cleanup requirements

### Documentation Quality Standards

- **Actionable Instructions**: Every procedure must include specific commands and expected outputs
- **Security First**: All credential handling must emphasize security best practices
- **Cross-Platform**: Instructions must work on Windows, macOS, and Linux
- **Beginner-Friendly**: Assume no prior Azure DevOps API experience
- **Maintainable**: Include version information and update procedures

### Code Examples Required

```csharp
// Template for new system test methods
[TestMethod]
[TestCategory("SystemTest")]
public async Task NewFeature_SystemTest_ValidScenario()
{
    // Environment validation
    // Test execution
    // Cleanup
}
```

```yaml
# GitHub Actions integration example
- name: System Tests
  if: github.ref == 'refs/heads/main'
  run: dotnet test --filter "TestCategory=SystemTest"
  env:
    AZDEVOPS_SYSTEM_TEST_ORG: ${{ secrets.AZDEVOPS_SYSTEM_TEST_ORG }}
    AZDEVOPS_SYSTEM_TEST_PAT: ${{ secrets.AZDEVOPS_SYSTEM_TEST_PAT }}
```

### Troubleshooting Matrix

| Error Type | Symptoms | Root Cause | Solution |
|------------|----------|------------|----------|
| Environment | Test skipped | Missing env vars | Set AZDEVOPS_SYSTEM_TEST_* variables |
| Authentication | 401 Unauthorized | Invalid/expired PAT | Regenerate token with correct scopes |
| Permissions | 403 Forbidden | Insufficient scopes | Update token with Project/Team read access |
| Network | Connection timeout | Connectivity issues | Verify internet and Azure DevOps accessibility |
| Organization | 404 Not Found | Invalid org name | Verify organization name and access |

### Update Procedures

- How to update documentation when adding new system test categories
- Process for updating CI/CD examples when workflow changes
- Maintenance schedule for verifying external links and commands