using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Manages the lifecycle of a single child process: start, health-check, stdout/stderr
/// capture, graceful shutdown, and exit monitoring.
///
/// Used by <see cref="LocalStackHost"/> to launch ControlPlane and MigrationAgent as
/// separate OS processes so that each component has its own <c>DiagnosticListener</c>
/// and OpenTelemetry pipeline — eliminating instrumentation bleed that occurs when
/// multiple OTel pipelines share a single process.
/// </summary>
internal sealed class ChildProcessHost : IAsyncDisposable
{
    private readonly string _displayName;
    private readonly string _exePath;
    private readonly Dictionary<string, string> _environmentVariables;
    private readonly ILogger? _logger;

    private Process? _process;
    private TaskCompletionSource<int>? _exitTcs;

    /// <summary>
    /// Creates a new <see cref="ChildProcessHost"/> for the given executable.
    /// </summary>
    /// <param name="displayName">Human-readable name for log messages (e.g. "ControlPlane", "MigrationAgent").</param>
    /// <param name="exePath">Absolute path to the executable.</param>
    /// <param name="environmentVariables">Environment variables to pass to the child process.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public ChildProcessHost(
        string displayName,
        string exePath,
        Dictionary<string, string> environmentVariables,
        ILogger? logger = null)
    {
        _displayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
        _environmentVariables = environmentVariables ?? throw new ArgumentNullException(nameof(environmentVariables));
        _logger = logger;
    }

    /// <summary>
    /// Whether the child process has exited (or was never started).
    /// </summary>
    public bool HasExited => _process is null || _process.HasExited;

    /// <summary>
    /// The process ID of the running child, or <c>null</c> if not started.
    /// </summary>
    public int? ProcessId => _process?.HasExited == false ? _process.Id : null;

    /// <summary>
    /// A task that completes when the child process exits. Returns the exit code.
    /// </summary>
    public Task<int>? Exited => _exitTcs?.Task;

    /// <summary>
    /// Starts the child process with stdout/stderr captured (not forwarded to the CLI console).
    /// </summary>
    public void Start()
    {
        if (_process is not null)
            throw new InvalidOperationException($"{_displayName} process is already started.");

        if (!File.Exists(_exePath))
            throw new FileNotFoundException($"{_displayName} executable not found at: {_exePath}", _exePath);

        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };

        foreach (var (key, value) in _environmentVariables)
            psi.Environment[key] = value;

        _exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _logger?.LogDebug("[{ProcessName}] {Line}", _displayName, e.Data);
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                _logger?.LogWarning("[{ProcessName}:stderr] {Line}", _displayName, e.Data);
        };

        _process.Exited += (_, _) =>
        {
            var exitCode = 0;
            try { exitCode = _process.ExitCode; } catch { /* process may have been disposed */ }
            _exitTcs.TrySetResult(exitCode);
        };

        if (!_process.Start())
            throw new InvalidOperationException($"Failed to start {_displayName} process.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _logger?.LogInformation("{ProcessName} started (PID {Pid})", _displayName, _process.Id);
    }

    /// <summary>
    /// Attempts graceful shutdown by killing the process tree. Waits up to
    /// <paramref name="timeout"/> for exit before forcibly killing.
    /// </summary>
    public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_process is null || _process.HasExited)
            return;

        _logger?.LogInformation("Stopping {ProcessName} (PID {Pid})...", _displayName, _process.Id);

        try
        {
            // .NET 10 supports killing the entire process tree.
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the HasExited check and Kill call.
            return;
        }

        // Wait for the process to actually exit.
        if (_exitTcs is not null)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await _exitTcs.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger?.LogWarning("{ProcessName} did not exit within {Timeout}; force-killed.", _displayName, timeout);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                await StopAsync(TimeSpan.FromSeconds(5));
            }

            _process.Dispose();
            _process = null;
        }
    }

    // ── Static helpers for executable discovery ─────────────────────────

    /// <summary>
    /// Resolves the absolute path to a sibling component executable.
    /// Searches in order:
    /// 1. <c>MIGRATION_{COMPONENT}_EXE</c> environment variable override.
    /// 2. Installed layout: <c>../ComponentName/ExeName</c> relative to the CLI binary.
    /// 3. Development layout: sibling project <c>bin/{config}/net10.0/ExeName</c>.
    /// Returns <c>null</c> if no executable is found.
    /// </summary>
    /// <param name="componentName">Directory name in the installed layout (e.g. "ControlPlane", "MigrationAgent").</param>
    /// <param name="assemblyName">Assembly name without extension (e.g. "DevOpsMigrationPlatform.ControlPlaneHost").</param>
    /// <returns>Absolute path to the executable, or <c>null</c> if not found.</returns>
    public static string? ResolveExecutablePath(string componentName, string assemblyName)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{assemblyName}.exe"
            : assemblyName;

        // 1. Environment variable override
        var envKey = $"MIGRATION_{componentName.ToUpperInvariant()}_EXE";
        var envPath = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        var cliDir = AppContext.BaseDirectory;

        // 2a. Installed layout (flat package): CLI at root, ComponentName/ subfolder
        var flatPath = Path.Combine(cliDir, componentName, exeName);
        if (File.Exists(flatPath))
            return Path.GetFullPath(flatPath);

        // 2b. Installed layout (sibling): ../ComponentName/ExeName
        var siblingPath = Path.Combine(cliDir, "..", componentName, exeName);
        if (File.Exists(siblingPath))
            return Path.GetFullPath(siblingPath);

        // 3. Development layout: look for sibling project output
        // CLI is at src/CLI.Migration/bin/{config}/net10.0/ — walk up to src/
        // and look for ComponentProject/bin/{config}/net10.0/ExeName
        var srcDir = FindSrcDirectory(cliDir);
        if (srcDir is not null)
        {
            // Try both Debug and Release
            foreach (var config in new[] { "Debug", "Release" })
            {
                var devPath = Path.Combine(srcDir, $"DevOpsMigrationPlatform.{componentName switch
                {
                    "ControlPlane" => "ControlPlaneHost",
                    "MigrationAgent" => "MigrationAgent",
                    _ => componentName
                }}", "bin", config, "net10.0", exeName);

                if (File.Exists(devPath))
                    return Path.GetFullPath(devPath);
            }
        }

        return null;
    }

    /// <summary>
    /// Walks up from a bin output directory to find the <c>src/</c> folder.
    /// </summary>
    private static string? FindSrcDirectory(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (dir.Name.Equals("src", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            // If we find a .slnx or .sln, src is a sibling
            if (dir.Parent is not null && Directory.Exists(Path.Combine(dir.Parent.FullName, "src")))
                return Path.Combine(dir.Parent.FullName, "src");
            dir = dir.Parent;
        }
        return null;
    }
}
