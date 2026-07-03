// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Validates that module options classes bind correctly from configuration
/// and surface validation errors for invalid inputs. (T098)
/// </summary>
[TestClass]
public sealed class ModuleOptionsConfigurationTests
{
    // ─── TeamsModuleOptions ──────────────────────────────────────────────────

    // TODO: [test-validity] Score 13/25 — Tests Microsoft.Extensions.Configuration binding plumbing via SectionName constant.
    // Rewrite to test: when SectionName binding is wrong (e.g. typo), module silently stays disabled → assert that a
    // realistic misconfiguration produces a log warning or validation error rather than silent no-op.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TeamsModuleOptions_BindsEnabled_FromConfiguration()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Teams:Enabled"] = "true"
        });

        var services = new ServiceCollection();
        services.Configure<TeamsModuleOptions>(
            config.GetSection(TeamsModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<TeamsModuleOptions>>().Value;

        // Assert
        Assert.IsTrue(opts.Enabled);
    }

    // TODO: [test-validity] Score 12/25 — Tests property initialiser defaults, not runtime or configuration behaviour.
    // Rewrite to test: when no Teams section is present in appsettings, assert that the migration run skips teams
    // (module disabled) rather than asserting raw property values.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TeamsModuleOptions_DefaultScope_IsAll()
    {
        // Arrange — no Scope key in configuration
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Teams:Enabled"] = "false"
        });

        var services = new ServiceCollection();
        services.Configure<TeamsModuleOptions>(
            config.GetSection(TeamsModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<TeamsModuleOptions>>().Value;

        // Assert — default values from property initialisers
        Assert.AreEqual("all", opts.Selection.Scope);
        Assert.AreEqual(string.Empty, opts.Selection.Filter);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TeamsModuleOptions_ExtensionDefaults_AreAllEnabled()
    {
        // Arrange — empty config
        var config = BuildConfig(new Dictionary<string, string?>());
        var services = new ServiceCollection();
        services.Configure<TeamsModuleOptions>(
            config.GetSection(TeamsModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<TeamsModuleOptions>>().Value;

        // Assert
        Assert.IsTrue(opts.Data.TeamSettings);
        Assert.IsTrue(opts.Processing.NodeTranslation);
        Assert.IsTrue(opts.Data.TeamIterations);
        Assert.IsTrue(opts.Data.TeamMembers);
        Assert.IsTrue(opts.Data.TeamCapacity);
    }

    // TODO: [test-validity] Score 15/25 — Tests that Scope and Filter strings bind from config — partially
    // redundant with BindsEnabled test. Rewrite to test: when Scope="teams" and Filter="^Foo", assert that only
    // matching teams are exported (exercise the filter regex logic, not just the property binding).
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TeamsModuleOptions_BindsScopeAndFilter_FromConfiguration()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Teams:Selection:Scope"] = "teams",
            ["MigrationPlatform:Modules:Teams:Selection:Filter"] = "^Platform"
        });
        var services = new ServiceCollection();
        services.Configure<TeamsModuleOptions>(
            config.GetSection(TeamsModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<TeamsModuleOptions>>().Value;

        // Assert
        Assert.AreEqual("teams", opts.Selection.Scope);
        Assert.AreEqual("^Platform", opts.Selection.Filter);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void TeamsModuleOptions_CanDisableIndividualExtensions()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Teams:Data:TeamCapacity"] = "false",
            ["MigrationPlatform:Modules:Teams:Processing:NodeTranslation"] = "false"
        });
        var services = new ServiceCollection();
        services.Configure<TeamsModuleOptions>(
            config.GetSection(TeamsModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<TeamsModuleOptions>>().Value;

        // Assert
        Assert.IsFalse(opts.Data.TeamCapacity);
        Assert.IsFalse(opts.Processing.NodeTranslation);
        Assert.IsTrue(opts.Data.TeamIterations, "Other extensions should remain at their defaults.");
    }

    // ─── NodesModuleOptions ──────────────────────────────────────────

    // TODO: [test-validity] Score 13/25 — Mirrors TeamsModuleOptions_BindsEnabled_FromConfiguration.
    // Rewrite to test: when NodeTranslation is enabled via config, NodesModule.ExportAsync
    // actually writes referenced-paths.json (observable outcome), rather than asserting raw property binding.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void NodesModuleOptions_BindsEnabled_FromConfiguration()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
            ["MigrationPlatform:Modules:Nodes:Processing:ReplicateSourceTree"] = "true"
        });
        var services = new ServiceCollection();
        services.Configure<NodesModuleOptions>(
            config.GetSection(NodesModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<NodesModuleOptions>>().Value;

        // Assert
        Assert.IsTrue(opts.Enabled);
        Assert.IsTrue(opts.Processing.ReplicateSourceTree);
    }

    // TODO: [test-validity] Score 13/25 — Tests property initialiser defaults. Rewrite to test: when NodeTranslation
    // module is configured with defaults (Enabled:true), assert it writes to the artefact store on ExportAsync.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void NodesModuleOptions_DefaultsAreCorrect()
    {
        // Arrange — empty config
        var config = BuildConfig(new Dictionary<string, string?>());
        var services = new ServiceCollection();
        services.Configure<NodesModuleOptions>(
            config.GetSection(NodesModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<NodesModuleOptions>>().Value;

        // Assert — Enabled defaults to true so the module runs without explicit config
        Assert.IsTrue(opts.Enabled);
        Assert.IsFalse(opts.Processing.ReplicateSourceTree);
    }

    // ─── IdentitiesModuleOptions ─────────────────────────────────────────────

    // TODO: [test-validity] Score 13/25 — Tests config binding of DefaultIdentity string. Rewrite to test: when
    // DefaultIdentity is set via config, IdentityMappingService.Resolve returns the default for an unknown descriptor
    // (exercise the mapping fallback, not just property binding).
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void IdentitiesModuleOptions_BindsDefaultIdentity_FromConfiguration()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Identities:Enabled"] = "true",
            ["MigrationPlatform:Modules:Identities:Processing:DefaultIdentity"] = "system@contoso.com"
        });
        var services = new ServiceCollection();
        services.Configure<IdentitiesModuleOptions>(
            config.GetSection(IdentitiesModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<IdentitiesModuleOptions>>().Value;

        // Assert
        Assert.IsTrue(opts.Enabled);
        Assert.AreEqual("system@contoso.com", opts.Processing.DefaultIdentity);
    }

    // TODO: [test-validity] Score 12/25 — Tests property initialiser default of DefaultIdentity="". Partially
    // redundant with BindsDefaultIdentity test. Rewrite to test: when DefaultIdentity is absent from config,
    // Resolve falls back to the source identity string, not an empty or null result.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void IdentitiesModuleOptions_DefaultIdentity_IsEmptyStringByDefault()
    {
        // Arrange — empty config
        var config = BuildConfig(new Dictionary<string, string?>());
        var services = new ServiceCollection();
        services.Configure<IdentitiesModuleOptions>(
            config.GetSection(IdentitiesModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<IdentitiesModuleOptions>>().Value;

        // Assert
        Assert.AreEqual(string.Empty, opts.Processing.DefaultIdentity);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}

