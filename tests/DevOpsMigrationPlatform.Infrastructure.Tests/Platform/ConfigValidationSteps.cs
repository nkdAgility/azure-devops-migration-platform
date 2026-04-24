using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

[Binding]
[Scope(Feature = "Migration Configuration Validation")]
public class ConfigValidationSteps
{
    private readonly ConfigValidationContext _ctx;

    public ConfigValidationSteps(ConfigValidationContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given(@"the platform is configured to validate on start")]
    public void GivenThePlatformIsConfiguredToValidateOnStart()
    {
        // Validator is created in context constructor; nothing extra to wire here.
    }

    // ── Given ─────────────────────────────────────────────────────────────────

    [Given(@"a migration config with mode ""(.*)""")]
    public void GivenAMigrationConfigWithMode(string mode)
    {
        _ctx.Options.Mode = mode;
    }

    [Given(@"the config has a source endpoint of type ""(.*)""")]
    public void GivenTheConfigHasASourceEndpointOfType(string type)
    {
        _ctx.Options.Source = new AzureDevOpsEndpointOptions
        {
            Type = type,
            Url = "https://dev.azure.com/myorg",
            Project = "MyProject"
        };
    }

    [Given(@"the config has a target endpoint of type ""(.*)""")]
    public void GivenTheConfigHasATargetEndpointOfType(string type)
    {
        _ctx.Options.Target = new AzureDevOpsEndpointOptions
        {
            Type = type,
            Url = "https://dev.azure.com/targetorg",
            Project = "TargetProject"
        };
    }

    [Given(@"the config has no source endpoint")]
    public void GivenTheConfigHasNoSourceEndpoint()
    {
        _ctx.Options.Source = null;
    }

    [Given(@"the config has no target endpoint")]
    public void GivenTheConfigHasNoTargetEndpoint()
    {
        _ctx.Options.Target = null;
    }

    [Given(@"the config has a package path of ""(.*)""")]
    public void GivenTheConfigHasAnArtefactsPathOf(string path)
    {
        _ctx.Options.Package.WorkingDirectory = path;
    }

    // ── When ──────────────────────────────────────────────────────────────────

    [When(@"the config is validated")]
    public void WhenTheConfigIsValidated()
    {
        _ctx.Result = _ctx.Sut.Validate(null, _ctx.Options);
    }

    // ── Then ──────────────────────────────────────────────────────────────────

    [Then(@"the validation passes")]
    public void ThenTheValidationPasses()
    {
        Assert.IsTrue(_ctx.Result!.Succeeded,
            $"Expected validation to pass but got failures: {_ctx.Result.FailureMessage}");
    }

    [Then(@"the validation fails")]
    public void ThenTheValidationFails()
    {
        Assert.IsFalse(_ctx.Result!.Succeeded,
            "Expected validation to fail but it passed.");
    }

    [Then(@"the error mentions ""(.*)""")]
    public void ThenTheErrorMentions(string keyword)
    {
        Assert.IsTrue(
            _ctx.Result!.FailureMessage?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true,
            $"Expected error message to mention '{keyword}' but got: {_ctx.Result.FailureMessage}");
    }
}
