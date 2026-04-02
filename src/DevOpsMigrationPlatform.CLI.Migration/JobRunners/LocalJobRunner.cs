using System.Runtime.CompilerServices;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI.JobRunners;

/// <summary>
/// Executes the Job Engine entirely in-process. No control plane required.
///
/// Used when the CLI is configured for Standalone mode — typically on a developer
/// laptop or in CI where spinning up the full Aspire stack is unnecessary.
///
/// The Job Engine has no knowledge of the transport; <see cref="LocalJobRunner"/>
/// wires up the stores, module graph, and progress sink and drives execution
/// directly. Progress events are streamed back to the caller as they are emitted.
///
/// See docs/cli.md and docs/orchestration.md.
/// </summary>
public sealed class LocalJobRunner : IJobRunner
{
    private readonly ILogger<LocalJobRunner> _logger;

    public LocalJobRunner(ILogger<LocalJobRunner> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ProgressEvent> RunAsync(
        MigrationJob job,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!job.Guardrails.StreamingRequired)
            throw new InvalidOperationException("MigrationJob.Guardrails.StreamingRequired must be true.");
        if (!job.Guardrails.CanonicalWorkItemsLayoutRequired)
            throw new InvalidOperationException("MigrationJob.Guardrails.CanonicalWorkItemsLayoutRequired must be true.");

        _logger.LogInformation("LocalJobRunner starting job {JobId} mode={Mode}", job.JobId, job.Mode);

        // Resolve the artefact store from the packageUri.
        // Currently only file:/// is supported (cloud blob store is net10.0-only AzureBlobArtefactStore).
        var store = ResolveArtefactStore(job);

        // Emit a synthetic start event so callers see activity immediately.
        yield return new ProgressEvent
        {
            Module = "JobEngine",
            Stage = "Started",
            Message = $"Job {job.JobId} started in LocalJobRunner"
        };

        // TODO: Implement full Job Engine execution (docs/orchestration.md):
        //   1. Build module dependency graph (topological sort on IDataTypeModule.DependsOn)
        //   2. Run pre-execution ValidateAsync per module
        //   3. For mode=Export or Both: ExportAsync per module in dependency order
        //   4. For mode=Both: package validation pass
        //   5. For mode=Import or Both: ImportAsync per module in dependency order
        //   6. Emit ProgressEvents after each cursor write

        await Task.CompletedTask; // placeholder until Job Engine is implemented

        yield return new ProgressEvent
        {
            Module = "JobEngine",
            Stage = "Completed",
            Message = $"Job {job.JobId} completed"
        };

        _logger.LogInformation("LocalJobRunner completed job {JobId}", job.JobId);
    }

    private static IArtefactStore ResolveArtefactStore(MigrationJob job)
    {
        var uri = job.Artefacts.PackageUri;

        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException("MigrationJob.Artefacts.PackageUri must not be empty.");

        if (uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = new Uri(uri).LocalPath;
            return new FileSystemArtefactStore(localPath);
        }

        throw new NotSupportedException(
            $"Package URI scheme is not supported by LocalJobRunner: {uri}. " +
            "Only file:/// is supported in local mode.");
    }
}
