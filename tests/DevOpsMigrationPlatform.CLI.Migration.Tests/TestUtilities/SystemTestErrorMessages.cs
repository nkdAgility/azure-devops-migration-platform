namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Centralized error message templates for system tests following contracts/test-interface.md requirements
/// </summary>
public static class SystemTestErrorMessages
{
    // Environment configuration error messages
    public static string EnvironmentNotConfigured => 
        "System test skipped: Environment variables not configured. " +
        "Set AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT to run this test. " +
        "See docs/contributors.md for setup instructions.";

    public static string MissingOrganizationVariable => 
        "Environment variable 'AZDEVOPS_SYSTEM_TEST_ORG' is not set or is empty.";

    public static string MissingTokenVariable => 
        "Environment variable 'AZDEVOPS_SYSTEM_TEST_PAT' is not set or is empty.";

    // Authentication and connectivity error messages
    public static string AuthenticationFailed(string organization) => 
        $"Authentication failed for organization '{organization}'. " +
        "Verify AZDEVOPS_SYSTEM_TEST_PAT token has required permissions. " +
        "See docs/contributors.md troubleshooting section.";

    public static string ConnectivityFailed(string organization, string error) => 
        $"Cannot connect to Azure DevOps organization '{organization}': {error}. " +
        "Verify network connectivity and organization accessibility.";

    public static string TokenResolutionFailed(string details) => 
        $"Token resolution failed: {details}";

    public static string InvalidTokenFormat => 
        "Invalid token format: Token appears too short to be valid";

    // Test execution error messages
    public static string TimeoutExceeded(string testName, int maxSeconds) => 
        $"System test '{testName}' exceeded maximum execution time of {maxSeconds} seconds";

    public static string TestExecutionFailed(string testName, string error) => 
        $"System test '{testName}' failed: {error}";

    public static string ArtifactCleanupWarning(string artifactPath, string error) => 
        $"Warning: Failed to cleanup artifact '{artifactPath}': {error}";

    // Success and informational messages
    public static string EnvironmentValidated(string testName) => 
        $"System test environment validated for '{testName}'";

    public static string TestCompleted(string testName, double durationSeconds) => 
        $"System test '{testName}' completed successfully in {durationSeconds:F2}s";

    public static string TokenResolutionSuccess => 
        "Token resolution validated using TokenResolver pattern";

    public static string ConnectivityValidated => 
        "Azure DevOps connectivity validated successfully";

    // CI/CD specific messages
    public static string CIEnvironmentDetected => 
        "CI environment detected - running system tests with repository secrets";

    public static string LocalEnvironmentDetected => 
        "Local environment detected - running system tests with environment variables";

    public static string SecretsNotAvailable => 
        "System test skipped in CI: Repository secrets not configured or not available";

    // Troubleshooting guidance
    public static string TroubleshootingGuidance => 
        "For setup instructions and troubleshooting, see docs/contributors.md";

    public static string PermissionsGuidance => 
        "Ensure your PAT token has 'Work Items (Read)' and 'Project and Team (Read)' permissions";

    public static string NetworkGuidance => 
        "If behind a corporate firewall, ensure access to dev.azure.com is allowed";
}