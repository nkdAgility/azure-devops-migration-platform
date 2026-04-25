using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Jobs;
#if !NET481
using System.Collections.Generic;
using System.Threading;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Abstraction for the job execution transport.
///
/// The only permitted implementation is <c>ControlPlaneClient</c>, which submits the
/// <see cref="MigrationJob"/> to a running control plane over HTTP, then streams
/// progress events back. The control plane is always present — in local/server mode
/// it is started in-process by the CLI via Aspire (http://localhost:5100); in cloud
/// mode it is a remote Azure Container Apps endpoint.
///
/// ⛔ Do NOT implement a <c>LocalJobRunner</c> or any in-process job executor.
///    Every topology — developer laptop, dedicated server, and cloud — requires the
///    control plane (ControlPlaneHost + PostgreSQL). There is no standalone mode
///    without a control plane. See guardrail rule #20 in
///    .agents/guardrails/system-architecture.md and docs/architecture.md.
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
        CancellationToken ct = default);
}
#endif
