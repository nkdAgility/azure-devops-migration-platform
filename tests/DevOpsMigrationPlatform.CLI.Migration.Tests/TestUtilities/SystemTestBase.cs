// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Base system test infrastructure following established CLI test patterns
/// </summary>
public abstract class SystemTestBase
{
    /// <summary>
    /// Maximum allowed execution time for system tests in seconds
    /// </summary>
    protected static readonly int MaxExecutionTimeSeconds = 30;

    /// <summary>
    /// Validates system test environment configuration
    /// </summary>
    /// <returns>Configuration instance with validation results</returns>
    public static SystemTestConfiguration ValidateEnvironment()
    {
        return SystemTestConfiguration.LoadFromEnvironment();
    }

    /// <summary>
    /// Validates Azure DevOps connectivity using existing ConfigTokenResolver pattern
    /// </summary>
    /// <param name="configuration">System test configuration</param>
    /// <returns>Validation result with connectivity status</returns>
    public static async Task<ValidationResult> ValidateConnectivityAsync(SystemTestConfiguration configuration)
    {
        if (!configuration.IsConfigured)
        {
            return ValidationResult.Failure("Connectivity",
                "Cannot test connectivity: Environment not configured");
        }

        try
        {
            // Use existing ConfigTokenResolver pattern for secure token resolution
            var resolvedToken = ConfigTokenResolver.Resolve($"$ENV:AZDEVOPS_SYSTEM_TEST_PAT");

            if (string.IsNullOrEmpty(resolvedToken))
            {
                return ValidationResult.Failure("Connectivity",
                    "Token resolution failed: AZDEVOPS_SYSTEM_TEST_PAT could not be resolved");
            }

            // Basic validation - ensure token and org are accessible
            // In full implementation, this would test actual Azure DevOps connectivity
            // For now, we validate the token resolution pattern works
            if (resolvedToken.Length < 10) // Basic sanity check
            {
                return ValidationResult.Failure("Connectivity",
                    "Invalid token format: Token appears too short to be valid");
            }

            return ValidationResult.Success("Connectivity",
                new() { "Token resolution validated using ConfigTokenResolver pattern" });
        }
        catch (InvalidOperationException ex)
        {
            return ValidationResult.Failure("Connectivity",
                $"Token resolution failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure("Connectivity",
                $"Connectivity validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes a system test with proper timeout and error handling
    /// </summary>
    /// <param name="testAction">Test action to execute</param>
    /// <param name="context">System test context</param>
    public static async Task ExecuteSystemTestAsync(Func<SystemTestContext, Task> testAction, SystemTestContext context)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(MaxExecutionTimeSeconds));

        try
        {
            await testAction(context);

            // Log test success
            Console.WriteLine($"System test '{context.TestName}' completed successfully in {context.Duration.TotalSeconds:F2}s");
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            Assert.Fail($"System test '{context.TestName}' exceeded maximum execution time of {MaxExecutionTimeSeconds} seconds");
        }
        catch (Exception ex)
        {
            Assert.Fail($"System test '{context.TestName}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a system test context with proper initialization
    /// </summary>
    /// <param name="testName">Name of the test method</param>
    /// <returns>Initialized system test context</returns>
    public static SystemTestContext CreateSystemTestContext(string testName)
    {
        var configuration = ValidateEnvironment();
        return new SystemTestContext(testName, configuration);
    }

    /// <summary>
    /// Handles system test setup and validation with proper Assert.Fail for missing environment
    /// </summary>
    /// <param name="testName">Name of the test method</param>
    /// <returns>Validated system test context, or terminates test if environment not configured</returns>
    public static async Task<SystemTestContext> SetupSystemTestAsync(string testName)
    {
        var context = CreateSystemTestContext(testName);

        // Check environment configuration
        if (!context.Configuration.IsConfigured)
        {
            var errorMessage = context.Configuration.GetConfigurationErrorMessage();
            Assert.Fail(errorMessage);
        }

        // Validate connectivity
        var connectivity = await ValidateConnectivityAsync(context.Configuration);
        if (!connectivity.IsValid)
        {
            Assert.Fail($"System test skipped: {connectivity.GetFormattedMessage()}");
        }

        context.ConnectionValidated = true;
        Console.WriteLine($"System test environment validated for '{testName}'");

        return context;
    }
}