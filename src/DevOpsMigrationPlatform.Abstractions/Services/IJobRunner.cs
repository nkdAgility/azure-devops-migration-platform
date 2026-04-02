#if !NET481
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Abstraction for the job execution transport.
/// Two implementations exist:
///
/// - <c>LocalJobRunner</c>  — executes the Job Engine in-process. No control plane
///   required. Used for Standalone mode (developer laptop, no Aspire).
///
/// - <c>ControlPlaneClient</c> — submits the MigrationJob to a running control plane
///   over HTTP, then polls for progress. Used for both local-Aspire mode
///   (http://localhost:5100) and cloud mode (Azure Container Apps).
///
/// Both transports accept the same <see cref="MigrationJob"/> payload.
/// Switching between them requires only a config change — no code changes.
/// See docs/cli.md and docs/architecture.md.
/// </summary>
public interface IJobRunner
{
    /// <summary>
    /// Submit and execute (or enqueue) the job.
    /// Returns an async stream of <see cref="ProgressEvent"/> items until the job
    /// completes, fails, or the token is cancelled.
    /// </summary>
    IAsyncEnumerable<ProgressEvent> RunAsync(
        MigrationJob job,
        [EnumeratorCancellation] CancellationToken ct = default);
}
#endif
