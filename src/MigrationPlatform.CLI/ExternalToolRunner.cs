using System.Diagnostics;


namespace MigrationPlatform.CLI
{
    public class ExternalToolRunner
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

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    onOutput?.Invoke(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    onError?.Invoke(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            return process.ExitCode;
        }
    }

}
