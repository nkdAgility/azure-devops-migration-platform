using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Launches an external executable and streams its stdout/stderr
/// back via callbacks. Returns the process exit code.
/// </summary>
public static class ExternalToolRunner
{
    public static async Task<int> RunWithStreamingAsync(
        string exePath,
        string arguments,
        string? stdinContent = null,
        Action<string>? onOutput = null,
        Action<string>? onError = null,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinContent != null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) onOutput?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) onError?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (stdinContent != null)
        {
            await process.StandardInput.WriteLineAsync(stdinContent).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (!process.HasExited)
            process.Kill();

        return process.ExitCode;
    }
}
