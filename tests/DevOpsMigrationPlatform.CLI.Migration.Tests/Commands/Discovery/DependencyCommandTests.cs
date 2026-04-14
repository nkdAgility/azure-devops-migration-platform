using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands.Discovery;

[TestClass]
public class DependencyCommandTests
{
    [TestMethod]
    public void DependencyCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        var command = new DependencyCommand();
        Assert.IsNotNull(command);
    }

    [TestMethod]
    [TestCategory("SystemTest")]
    [Timeout(300_000)]
    public async Task DependencyCommand_SystemTest_AdoSingleProject_ExecutesSuccessfully()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT")))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT environment variables must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var repoRoot = CliRunner.FindRepoRoot();
        var scenarioPath = Path.Combine(repoRoot, "scenarios", "discovery-dependency-ado-single-project.json");
        
        if (!File.Exists(scenarioPath))
        {
            Assert.Inconclusive($"Scenario file not found: {scenarioPath}");
        }

        var outputDir = Path.Combine(repoRoot, "output");
        Directory.CreateDirectory(outputDir);

        var result = await CliRunner.RunAsync(
            args: ["discovery", "dependencies", "--config", "scenarios/discovery-dependency-ado-single-project.json", "--output", outputDir],
            timeout: TimeSpan.FromMinutes(4));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        Assert.IsFalse(result.TimedOut,
            "CLI timed out. The dependency discovery is either hung or the organisation is very large.");

        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("completed", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message not found in output.");

        var depsPath = Path.Combine(outputDir, "dependencies.csv");
        Assert.IsTrue(File.Exists(depsPath),
            $"dependencies.csv was not created at {depsPath}");

        var csvLines = File.ReadAllLines(depsPath);
        Console.WriteLine($"CSV path   : {depsPath}");
        Console.WriteLine($"CSV lines  : {csvLines.Length}");

        Assert.IsTrue(csvLines.Length >= 1,
            $"dependencies.csv should have at least a header row, but has {csvLines.Length} line(s).");

        var header = csvLines[0];
        Assert.IsTrue(header.Contains("SourceProject", StringComparison.OrdinalIgnoreCase),
            $"CSV header does not contain 'SourceProject'. Header: {header}");
    }
}
