using System.Diagnostics;
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
        Action<string>? onOutput = null,
        Action<string>? onError = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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

        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
