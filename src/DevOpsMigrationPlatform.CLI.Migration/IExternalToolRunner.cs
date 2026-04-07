using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Launches an external executable and streams its stdout/stderr back via callbacks.
/// </summary>
/// <remarks>
/// Intentionally placed in the <c>DevOpsMigrationPlatform.CLI</c> assembly rather than
/// <c>Abstractions</c>. OS process management (exit codes, stdout/stderr callbacks) is a
/// CLI infrastructure concern with no domain meaning. Moving it to <c>Abstractions</c>
/// would introduce infrastructure vocabulary into the domain layer.
/// </remarks>
public interface IExternalToolRunner
{
    /// <summary>
    /// Runs <paramref name="exePath"/> with <paramref name="arguments"/>, streaming
    /// output and error lines to the supplied callbacks.
    /// </summary>
    /// <returns>The process exit code.</returns>
    Task<int> RunWithStreamingAsync(
        string exePath,
        string arguments,
        string? stdinContent = null,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken cancellationToken = default);
}
