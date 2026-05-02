// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Runtime execution context for individual system tests
/// </summary>
public class SystemTestContext : IDisposable
{
    /// <summary>
    /// Name of the executing test method
    /// </summary>
    public string TestName { get; }

    /// <summary>
    /// Environment configuration instance
    /// </summary>
    public SystemTestConfiguration Configuration { get; }

    /// <summary>
    /// Temporary directory for test artifacts
    /// </summary>
    public string OutputDirectory { get; private set; }

    /// <summary>
    /// Test execution start timestamp
    /// </summary>
    public DateTime TestStartTime { get; }

    /// <summary>
    /// Whether Azure DevOps connectivity was verified
    /// </summary>
    public bool ConnectionValidated { get; set; }

    /// <summary>
    /// Test artifacts that need cleanup
    /// </summary>
    private readonly ConcurrentBag<string> _artifacts = new();

    public SystemTestContext(string testName, SystemTestConfiguration configuration)
    {
        TestName = testName ?? throw new ArgumentNullException(nameof(testName));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        TestStartTime = DateTime.UtcNow;

        // Create temporary output directory
        OutputDirectory = CreateTempDirectory();
    }

    /// <summary>
    /// Creates a temporary directory for test artifacts
    /// </summary>
    private string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "SystemTests", TestName, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempPath);
        _artifacts.Add(tempPath);
        return tempPath;
    }

    /// <summary>
    /// Registers an artifact for cleanup during disposal
    /// </summary>
    public void RegisterArtifact(string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            _artifacts.Add(path);
        }
    }

    /// <summary>
    /// Gets test execution duration
    /// </summary>
    public TimeSpan Duration => DateTime.UtcNow - TestStartTime;

    public void Dispose()
    {
        // Clean up test artifacts
        foreach (var artifact in _artifacts)
        {
            try
            {
                if (Directory.Exists(artifact))
                    Directory.Delete(artifact, recursive: true);
                else if (File.Exists(artifact))
                    File.Delete(artifact);
            }
            catch (Exception ex)
            {
                // Log cleanup failures but don't fail the test
                Console.WriteLine($"Warning: Failed to cleanup artifact '{artifact}': {ex.Message}");
            }
        }
    }
}