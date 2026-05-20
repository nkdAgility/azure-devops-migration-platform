// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.MigrationAgent.Tests;

[TestClass]
[DoNotParallelize]
public class SystemSmokeTests
{
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Smoke")]
    public async Task SystemTest_Smoke_AgentStartsWithoutStartupOrDiErrors()
    {
        var repoRoot = FindRepoRoot();
        var controlPlaneExe = ResolveComponentExe(
            repoRoot,
            "DevOpsMigrationPlatform.ControlPlaneHost",
            "DevOpsMigrationPlatform.ControlPlaneHost.exe");
        var agentExe = ResolveComponentExe(
            repoRoot,
            "DevOpsMigrationPlatform.MigrationAgent",
            "DevOpsMigrationPlatform.MigrationAgent.exe");

        var port = GetAvailablePort();
        var controlPlaneUrl = $"http://localhost:{port}";

        var cpStdout = new StringBuilder();
        var cpStderr = new StringBuilder();
        using var controlPlane = StartProcess(
            executablePath: controlPlaneExe,
            arguments: [],
            environment: new Dictionary<string, string>
            {
                ["ASPNETCORE_URLS"] = controlPlaneUrl,
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["AgentLifecycle__AutoSpawn"] = "false",
            },
            stdout: cpStdout,
            stderr: cpStderr);

        try
        {
            await WaitForHealthAsync(controlPlaneUrl, TimeSpan.FromSeconds(15));

            var agentStdout = new StringBuilder();
            var agentStderr = new StringBuilder();
            using var agent = StartProcess(
                executablePath: agentExe,
                arguments: [$"--ControlPlane:BaseUrl={controlPlaneUrl}"],
                environment: new Dictionary<string, string>
                {
                    ["DOTNET_ENVIRONMENT"] = "Development",
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                },
                stdout: agentStdout,
                stderr: agentStderr);

            try
            {
                var startupWindow = Task.Delay(TimeSpan.FromSeconds(5));
                var exitedTask = agent.WaitForExitAsync();
                var completedTask = await Task.WhenAny(startupWindow, exitedTask);
                if (completedTask == exitedTask)
                {
                    var startupOutput = agentStdout.ToString() + Environment.NewLine + agentStderr;
                    Assert.Fail(
                        $"MigrationAgent exited during smoke startup with code {agent.ExitCode}.{Environment.NewLine}" +
                        $"Agent output:{Environment.NewLine}{startupOutput}{Environment.NewLine}" +
                        $"ControlPlane output:{Environment.NewLine}{cpStdout}{Environment.NewLine}{cpStderr}");
                }

                var combinedAgentOutput = agentStdout.ToString() + Environment.NewLine + agentStderr;
                Assert.IsFalse(
                    combinedAgentOutput.Contains("Some services are not able to be constructed", StringComparison.OrdinalIgnoreCase),
                    $"Agent DI validation error detected during smoke startup:{Environment.NewLine}{combinedAgentOutput}");
                Assert.IsFalse(
                    combinedAgentOutput.Contains("Startup failed", StringComparison.OrdinalIgnoreCase),
                    $"Agent startup failure detected during smoke startup:{Environment.NewLine}{combinedAgentOutput}");
            }
            finally
            {
                StopProcess(agent);
            }
        }
        finally
        {
            StopProcess(controlPlane);
        }
    }

    private static Process StartProcess(
        string executablePath,
        IReadOnlyList<string> arguments,
        IDictionary<string, string> environment,
        StringBuilder stdout,
        StringBuilder stderr)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var (key, value) in environment)
            startInfo.Environment[key] = value;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                stdout.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                stderr.AppendLine(args.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process '{executablePath}'.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitForHealthAsync(string controlPlaneUrl, TimeSpan timeout)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(controlPlaneUrl),
            Timeout = TimeSpan.FromSeconds(2),
        };

        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            try
            {
                using var response = await httpClient.GetAsync("/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(200);
        }

        Assert.Fail($"ControlPlane did not become healthy at {controlPlaneUrl} within {timeout.TotalSeconds:0} seconds.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string ResolveComponentExe(string repoRoot, string projectName, string exeName)
    {
        var candidates = new[]
        {
            Path.Combine(repoRoot, "src", projectName, "bin", "Debug", "net10.0", exeName),
            Path.Combine(repoRoot, "src", projectName, "bin", "Release", "net10.0", exeName),
        };

        var existingCandidates = candidates
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(fileInfo => fileInfo.LastWriteTimeUtc)
            .ToList();

        if (existingCandidates.Count > 0)
            return existingCandidates[0].FullName;

        throw new FileNotFoundException(
            $"Could not resolve executable '{exeName}'. Expected at one of:{Environment.NewLine}{string.Join(Environment.NewLine, candidates)}");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevOpsMigrationPlatform.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root (no DevOpsMigrationPlatform.slnx found walking up from {AppContext.BaseDirectory}).");
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (process.HasExited)
                return;

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
