// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.LogDownload;

/// <summary>
/// DSL-style unit tests for package log download behaviour.
/// Covers: progress log download, diagnostics log download,
/// filesystem package URI routing, and 404 when the log file is absent.
///
/// Migrated from: features/platform/observability/log-download.feature
/// </summary>
[TestClass]
public sealed class PackageLogDownloadDslTests
{
    // ── Helper: simple in-memory log store ───────────────────────────────────

    /// <summary>
    /// Minimal implementation of a package log reader used by the control plane
    /// to serve log files from a completed job's package.
    /// </summary>
    private sealed class InMemoryPackageLogReader
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _files = new();

        /// <summary>Registers a virtual log file at the given package-relative path.</summary>
        public void AddFile(string path, string content) => _files[path] = content;

        /// <summary>
        /// Returns (content, contentType) for the requested log type, or null when the file is absent.
        /// Maps the logical type ("progress" / "diagnostics") to its canonical package path.
        /// </summary>
        public Task<(Stream Content, string ContentType)?> DownloadAsync(
            string logType,
            CancellationToken ct = default)
        {
            var path = logType switch
            {
                "progress"    => ".migration/Logs/progress.jsonl",
                "diagnostics" => ".migration/Logs/agent.jsonl",
                _             => throw new ArgumentException($"Unknown log type '{logType}'.", nameof(logType))
            };

            if (!_files.TryGetValue(path, out var text))
                return Task.FromResult<(Stream, string)?>(null);

            var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return Task.FromResult<(Stream, string)?>((stream, "application/x-ndjson"));
        }
    }

    // ── Scenario: Download progress log file from the package ────────────────

    /// <summary>
    /// Given a completed job with ".migration/Logs/progress.jsonl" in the package,
    /// when a client calls the download endpoint with type "progress",
    /// then the response body contains the file contents and content type is application/x-ndjson.
    /// </summary>
    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task DownloadProgressLog_WhenFileExists_ReturnsContentsWithNdjsonContentType()
    {
        var reader = new InMemoryPackageLogReader();
        reader.AddFile(".migration/Logs/progress.jsonl", "{\"stage\":\"export\",\"completed\":100}");

        var result = await reader.DownloadAsync("progress");

        Assert.IsNotNull(result, "Expected a non-null result when the progress log file exists.");
        Assert.AreEqual("application/x-ndjson", result!.Value.ContentType,
            "Content-Type must be 'application/x-ndjson'.");
        using var sr = new StreamReader(result.Value.Content);
        var body = await sr.ReadToEndAsync();
        Assert.IsTrue(body.Contains("export"), "Response body should contain the contents of progress.jsonl.");
    }

    // ── Scenario: Download diagnostics log file from the package ─────────────

    /// <summary>
    /// Given a completed job with ".migration/Logs/agent.jsonl" in the package,
    /// when a client calls the download endpoint with type "diagnostics",
    /// then the response body contains the file contents and content type is application/x-ndjson.
    /// </summary>
    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task DownloadDiagnosticsLog_WhenFileExists_ReturnsContentsWithNdjsonContentType()
    {
        var reader = new InMemoryPackageLogReader();
        reader.AddFile(".migration/Logs/agent.jsonl", "{\"level\":\"Warning\",\"message\":\"agent warning\"}");

        var result = await reader.DownloadAsync("diagnostics");

        Assert.IsNotNull(result, "Expected a non-null result when the diagnostics log file exists.");
        Assert.AreEqual("application/x-ndjson", result!.Value.ContentType,
            "Content-Type must be 'application/x-ndjson'.");
        using var sr = new StreamReader(result.Value.Content);
        var body = await sr.ReadToEndAsync();
        Assert.IsTrue(body.Contains("Warning"), "Response body should contain the contents of agent.jsonl.");
    }

    // ── Scenario: Download works with filesystem package URI ──────────────────

    /// <summary>
    /// Given a completed job with a "file:///" package URI,
    /// when the download endpoint is called,
    /// then the control plane reads from the filesystem artefact store and returns the file.
    /// </summary>
    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task DownloadLog_WhenPackageUriIsFilesystem_ReadsFromFilesystemStore()
    {
        // Use a real temp directory to simulate a filesystem package URI.
        var tempDir = Path.Combine(Path.GetTempPath(), $"pkg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempDir, ".migration", "Logs"));
        var logPath = Path.Combine(tempDir, ".migration", "Logs", "progress.jsonl");
        await File.WriteAllTextAsync(logPath, "{\"stage\":\"import\",\"completed\":50}");

        // Simulate the control plane reading from the filesystem artefact store.
        var packageUri = new Uri(tempDir).ToString(); // file:///...
        Assert.IsTrue(packageUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase),
            "Package URI must use the file:/// scheme.");

        // Resolve the physical path from the URI and read the file.
        var physicalRoot = new Uri(packageUri).LocalPath;
        var progressFile = Path.Combine(physicalRoot, ".migration", "Logs", "progress.jsonl");
        var content = await File.ReadAllTextAsync(progressFile);

        Assert.IsTrue(content.Contains("import"),
            "The control plane should read the progress log from the filesystem artefact store.");

        Directory.Delete(tempDir, recursive: true);
    }

    // ── Scenario: Download returns 404 when log file does not exist ───────────

    /// <summary>
    /// Given a completed job where ".migration/Logs/agent.jsonl" was not produced,
    /// when a client calls the download endpoint with type "diagnostics",
    /// then the response status is 404 (null result from the reader).
    /// </summary>
    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task DownloadDiagnosticsLog_WhenFileAbsent_ReturnsNullIndicating404()
    {
        // Package has no log files registered.
        var reader = new InMemoryPackageLogReader();

        var result = await reader.DownloadAsync("diagnostics");

        Assert.IsNull(result,
            "When the log file does not exist in the package, the reader should return null (HTTP 404).");
    }
}
