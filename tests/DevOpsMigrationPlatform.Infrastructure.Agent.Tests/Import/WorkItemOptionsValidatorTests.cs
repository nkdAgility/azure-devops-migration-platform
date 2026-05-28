// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public class WorkItemOptionsValidatorTests
{
    private static WorkItemOptionsValidator Sut() => new();

    [TestMethod]
    public void Validate_AllLeversDisabled_Succeeds()
    {
        var options = new WorkItemOptions();

        var result = Sut().Validate(name: null, options);

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_RevisionReplayEnabledWithDependentLevers_Succeeds()
    {
        var options = new WorkItemOptions
        {
            RevisionReplay = true,
            LinkReplay = true,
            AttachmentReplay = true,
            EmbeddedImageReplay = true,
            FieldTransform = true
        };

        var result = Sut().Validate(name: null, options);

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_RevisionReplayDisabledAndLinkReplayEnabled_Fails()
    {
        var options = new WorkItemOptions
        {
            RevisionReplay = false,
            LinkReplay = true
        };

        var result = Sut().Validate(name: null, options);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "LinkReplay requires RevisionReplay");
    }

    [TestMethod]
    public void Validate_RevisionReplayDisabledAndAttachmentReplayEnabled_Fails()
    {
        var options = new WorkItemOptions
        {
            RevisionReplay = false,
            AttachmentReplay = true
        };

        var result = Sut().Validate(name: null, options);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "AttachmentReplay requires RevisionReplay");
    }

    [TestMethod]
    public void Validate_RevisionReplayDisabledAndEmbeddedImageReplayEnabled_Fails()
    {
        var options = new WorkItemOptions
        {
            RevisionReplay = false,
            EmbeddedImageReplay = true
        };

        var result = Sut().Validate(name: null, options);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "EmbeddedImageReplay requires RevisionReplay");
    }

    [TestMethod]
    public void Validate_RevisionReplayDisabledAndFieldTransformEnabled_Fails()
    {
        var options = new WorkItemOptions
        {
            RevisionReplay = false,
            FieldTransform = true
        };

        var result = Sut().Validate(name: null, options);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("|", result.Failures!), "FieldTransform requires RevisionReplay");
    }

    [TestMethod]
    public void Validate_RevisionReplayDisabledAndAllDependentLeversEnabled_ReportsAllFailures()
    {
        var options = new WorkItemOptions
        {
            RevisionReplay = false,
            LinkReplay = true,
            AttachmentReplay = true,
            EmbeddedImageReplay = true,
            FieldTransform = true
        };

        var result = Sut().Validate(name: null, options);

        Assert.IsFalse(result.Succeeded);
        var failures = string.Join("|", result.Failures!);
        StringAssert.Contains(failures, "LinkReplay requires RevisionReplay");
        StringAssert.Contains(failures, "AttachmentReplay requires RevisionReplay");
        StringAssert.Contains(failures, "EmbeddedImageReplay requires RevisionReplay");
        StringAssert.Contains(failures, "FieldTransform requires RevisionReplay");
    }

    [TestMethod]
    public void AddWorkItemsModule_RegistersWorkItemOptionsValidator()
    {
        var services = new ServiceCollection();

        services.AddWorkItemsModule(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IValidateOptions<WorkItemOptions>>();

        Assert.IsTrue(validators.Any(v => v is WorkItemOptionsValidator));
    }

    [TestMethod]
    public void AddWorkItemsModule_InvalidWorkItemOptions_ThrowsOptionsValidationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{WorkItemOptions.SectionName}:RevisionReplay"] = "false",
                [$"{WorkItemOptions.SectionName}:LinkReplay"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddWorkItemsModule(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<WorkItemOptions>>();

        Assert.ThrowsExactly<OptionsValidationException>(() => _ = options.Value);
    }
}
