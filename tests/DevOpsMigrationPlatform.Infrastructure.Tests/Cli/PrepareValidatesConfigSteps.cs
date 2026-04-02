using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Cli;

[Binding]
[Scope(Feature = "CLI loads and applies migration configuration")]
public class PrepareValidatesConfigSteps
{
    private readonly PrepareValidatesConfigContext _ctx;
    private string? _userConfigJson;

    public PrepareValidatesConfigSteps(PrepareValidatesConfigContext ctx) => _ctx = ctx;

    // ── Given ─────────────────────────────────────────────────────────────────

    [Given(@"no migration\.json file exists")]
    public void GivenNoMigrationJsonFileExists()
    {
        _userConfigJson = null;
    }

    [Given(@"a migration\.json with artefacts path ""(.*)""")]
    public void GivenAMigrationJsonWithArtefactsPath(string path)
    {
        _userConfigJson = $$"""
            {
              "Artefacts": { "Path": "{{path.Replace("\\", "\\\\")}}" }
            }
            """;
    }

    [Given(@"a migration\.json with max retries (\d+)")]
    public void GivenAMigrationJsonWithMaxRetries(int max)
    {
        _userConfigJson = $$"""
            {
              "Policies": { "Retries": { "Max": {{max}} } }
            }
            """;
    }

    // ── When ──────────────────────────────────────────────────────────────────

    [When(@"the platform options are loaded from defaults only")]
    public void WhenThePlatformOptionsAreLoadedFromDefaultsOnly()
    {
        _ctx.LoadFromDefaultsOnly(PrepareValidatesConfigContext.DefaultsJson);
    }

    [When(@"the platform options are loaded")]
    public void WhenThePlatformOptionsAreLoaded()
    {
        if (_userConfigJson is null)
            _ctx.LoadFromDefaultsOnly(PrepareValidatesConfigContext.DefaultsJson);
        else
            _ctx.LoadWithUserConfig(PrepareValidatesConfigContext.DefaultsJson, _userConfigJson);
    }

    // ── Then ──────────────────────────────────────────────────────────────────

    [Then(@"the artefacts path is ""(.*)""")]
    public void ThenTheArtefactsPathIs(string expected)
    {
        Assert.AreEqual(expected, _ctx.ResolvedOptions!.Artefacts.Path);
    }

    [Then(@"the max retries is (\d+)")]
    public void ThenTheMaxRetriesIs(int expected)
    {
        Assert.AreEqual(expected, _ctx.ResolvedOptions!.Policies.Retries.Max);
    }

    [Then(@"the max concurrency is (\d+)")]
    public void ThenTheMaxConcurrencyIs(int expected)
    {
        Assert.AreEqual(expected, _ctx.ResolvedOptions!.Policies.Throttle.MaxConcurrency);
    }

    [Then(@"the expanded artefacts path does not contain ""%TEMP%""")]
    public void ThenTheExpandedArtefactsPathDoesNotContainTempVariable()
    {
        var expanded = _ctx.ResolvedOptions!.Artefacts.ExpandedPath;
        Assert.IsFalse(expanded.Contains("%TEMP%", StringComparison.OrdinalIgnoreCase),
            $"Expected expanded path but got: {expanded}");
    }

    [Then(@"the expanded artefacts path contains the value of the TEMP environment variable")]
    public void ThenTheExpandedArtefactsPathContainsTheTempValue()
    {
        var tempValue = Environment.GetEnvironmentVariable("TEMP")
                     ?? Environment.GetEnvironmentVariable("TMP")
                     ?? string.Empty;
        var expanded = _ctx.ResolvedOptions!.Artefacts.ExpandedPath;
        Assert.IsTrue(expanded.Contains(tempValue, StringComparison.OrdinalIgnoreCase),
            $"Expected '{expanded}' to contain TEMP value '{tempValue}'");
    }
}
