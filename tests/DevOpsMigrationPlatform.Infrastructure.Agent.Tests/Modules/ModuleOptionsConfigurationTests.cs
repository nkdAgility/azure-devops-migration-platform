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
[TestCategory("UnitTest")]
public sealed class ModuleOptionsConfigurationTests
{
    // ─── TeamsModuleOptions ──────────────────────────────────────────────────

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
        Assert.AreEqual("all", opts.Scope);
        Assert.AreEqual(string.Empty, opts.Filter);
    }

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
        Assert.IsTrue(opts.Extensions.TeamSettings);
        Assert.IsTrue(opts.Extensions.NodeStructure);
        Assert.IsTrue(opts.Extensions.TeamIterations);
        Assert.IsTrue(opts.Extensions.TeamMembers);
        Assert.IsTrue(opts.Extensions.TeamCapacity);
    }

    [TestMethod]
    public void TeamsModuleOptions_BindsScopeAndFilter_FromConfiguration()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Teams:Scope"] = "teams",
            ["MigrationPlatform:Modules:Teams:Filter"] = "^Platform"
        });
        var services = new ServiceCollection();
        services.Configure<TeamsModuleOptions>(
            config.GetSection(TeamsModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<TeamsModuleOptions>>().Value;

        // Assert
        Assert.AreEqual("teams", opts.Scope);
        Assert.AreEqual("^Platform", opts.Filter);
    }

    [TestMethod]
    public void TeamsModuleOptions_CanDisableIndividualExtensions()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Teams:Extensions:TeamCapacity"] = "false",
            ["MigrationPlatform:Modules:Teams:Extensions:NodeStructure"] = "false"
        });
        var services = new ServiceCollection();
        services.Configure<TeamsModuleOptions>(
            config.GetSection(TeamsModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<TeamsModuleOptions>>().Value;

        // Assert
        Assert.IsFalse(opts.Extensions.TeamCapacity);
        Assert.IsFalse(opts.Extensions.NodeStructure);
        Assert.IsTrue(opts.Extensions.TeamIterations, "Other extensions should remain at their defaults.");
    }

    // ─── NodeStructureModuleOptions ──────────────────────────────────────────

    [TestMethod]
    public void NodeStructureModuleOptions_BindsEnabled_FromConfiguration()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Nodes:Enabled"] = "true",
            ["MigrationPlatform:Modules:Nodes:ReplicateSourceTree"] = "true",
            ["MigrationPlatform:Modules:Nodes:AutoCreateNodes"] = "false"
        });
        var services = new ServiceCollection();
        services.Configure<NodeStructureModuleOptions>(
            config.GetSection(NodeStructureModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<NodeStructureModuleOptions>>().Value;

        // Assert
        Assert.IsTrue(opts.Enabled);
        Assert.IsTrue(opts.ReplicateSourceTree);
        Assert.IsFalse(opts.AutoCreateNodes);
    }

    [TestMethod]
    public void NodeStructureModuleOptions_DefaultsAreFalse()
    {
        // Arrange — empty config
        var config = BuildConfig(new Dictionary<string, string?>());
        var services = new ServiceCollection();
        services.Configure<NodeStructureModuleOptions>(
            config.GetSection(NodeStructureModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<NodeStructureModuleOptions>>().Value;

        // Assert
        Assert.IsFalse(opts.Enabled);
        Assert.IsFalse(opts.ReplicateSourceTree);
        Assert.IsFalse(opts.AutoCreateNodes);
    }

    // ─── IdentitiesModuleOptions ─────────────────────────────────────────────

    [TestMethod]
    public void IdentitiesModuleOptions_BindsDefaultIdentity_FromConfiguration()
    {
        // Arrange
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["MigrationPlatform:Modules:Identities:Enabled"] = "true",
            ["MigrationPlatform:Modules:Identities:DefaultIdentity"] = "system@contoso.com"
        });
        var services = new ServiceCollection();
        services.Configure<IdentitiesModuleOptions>(
            config.GetSection(IdentitiesModuleOptions.SectionName));
        var sp = services.BuildServiceProvider();

        // Act
        var opts = sp.GetRequiredService<IOptions<IdentitiesModuleOptions>>().Value;

        // Assert
        Assert.IsTrue(opts.Enabled);
        Assert.AreEqual("system@contoso.com", opts.DefaultIdentity);
    }

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
        Assert.AreEqual(string.Empty, opts.DefaultIdentity);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
