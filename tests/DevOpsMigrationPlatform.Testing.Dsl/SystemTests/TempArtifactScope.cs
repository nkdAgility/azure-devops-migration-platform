// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Wraps a temporary artifact directory lifecycle for system test scenarios.
/// Creates a directory on construction and deletes it on disposal.
/// </summary>
public sealed class TempArtifactScope : IDisposable
{
    private readonly List<string> _registeredArtifacts = new();
    private bool _disposed;

    private TempArtifactScope(string testName)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "SystemTests",
            testName,
            Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(path);
        OutputDirectory = path;
        _registeredArtifacts.Add(path);
    }

    /// <summary>Temporary directory created for this scope.</summary>
    public string OutputDirectory { get; }

    /// <summary>All paths registered with this scope (includes the root directory).</summary>
    public IReadOnlyList<string> RegisteredArtifacts => _registeredArtifacts.AsReadOnly();

    /// <summary>Creates a new scope for the given test name and registers the output directory.</summary>
    public static TempArtifactScope Create(string testName)
        => new(testName);

    /// <summary>Registers an additional artifact path for cleanup and assertion.</summary>
    public TempArtifactScope RegisterArtifact(string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            _registeredArtifacts.Add(path);
        }

        return this;
    }

    /// <summary>Asserts that all registered paths currently exist on disk.</summary>
    public TempArtifactScope ShouldHaveCreatedArtifacts()
    {
        foreach (var path in _registeredArtifacts)
        {
            Assert.IsTrue(
                Directory.Exists(path) || File.Exists(path),
                $"Expected artifact to exist: {path}");
        }

        return this;
    }

    /// <summary>
    /// Disposes the scope (deletes all registered paths) then asserts that none remain.
    /// </summary>
    public void ShouldHaveCleanedUpAllArtifacts()
    {
        Dispose();

        foreach (var path in _registeredArtifacts)
        {
            Assert.IsFalse(
                Directory.Exists(path) || File.Exists(path),
                $"Expected artifact to be cleaned up but it still exists: {path}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var path in _registeredArtifacts)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to clean up artifact '{path}': {ex.Message}");
            }
        }
    }
}
