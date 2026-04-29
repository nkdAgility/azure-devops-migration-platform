using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

[Binding]
[Scope(Feature = "Localised area and iteration node name override")]
public class LanguageOverrideSteps
{
    private readonly LanguageOverrideContext _ctx;

    public LanguageOverrideSteps(LanguageOverrideContext ctx) => _ctx = ctx;

    [Given(@"a source project named ""(.*)"" and a target project named ""(.*)""")]
    public void GivenSourceAndTargetProjects(string source, string target)
    {
        _ctx.SourceProjectName = source;
        _ctx.TargetProjectName = target;
    }

    [Given(@"a NodeTranslation tool configured with AreaLanguageOverride ""(.*)""")]
    public void GivenAreaLanguageOverride(string overrideValue)
    {
        _ctx.AreaLanguageOverride = overrideValue;
    }

    [Given(@"a NodeTranslation tool configured with IterationLanguageOverride ""(.*)""")]
    public void GivenIterationLanguageOverride(string overrideValue)
    {
        _ctx.IterationLanguageOverride = overrideValue;
    }

    [When(@"the tool translates area path ""(.*)""")]
    public void WhenToolTranslatesAreaPath(string path)
    {
        _ctx.TranslateAreaPath(path);
    }

    [When(@"the tool translates iteration path ""(.*)""")]
    public void WhenToolTranslatesIterationPath(string path)
    {
        _ctx.TranslateIterationPath(path);
    }

    [Then(@"the translated area path starts with ""(.*)""")]
    public void ThenTranslatedAreaPathStartsWith(string prefix)
    {
        Assert.IsNotNull(_ctx.TranslatedAreaPath);
        Assert.IsTrue(
            _ctx.TranslatedAreaPath!.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase),
            $"Expected translated area path to start with '{prefix}' but got '{_ctx.TranslatedAreaPath}'.");
    }

    [Then(@"the translated iteration path starts with ""(.*)""")]
    public void ThenTranslatedIterationPathStartsWith(string prefix)
    {
        Assert.IsNotNull(_ctx.TranslatedIterationPath);
        Assert.IsTrue(
            _ctx.TranslatedIterationPath!.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase),
            $"Expected translated iteration path to start with '{prefix}' but got '{_ctx.TranslatedIterationPath}'.");
    }
}
