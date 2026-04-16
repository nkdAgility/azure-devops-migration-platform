using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Cli;

public class PrepareValidatesConfigContext
{
    // Holds the resolved options after loading configuration layers.
    public MigrationOptions? ResolvedOptions { get; set; }

    // Path to a temp directory used for test json files.
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public PrepareValidatesConfigContext()
        => Directory.CreateDirectory(_tempDir);

    /// <summary>
    /// Builds an <see cref="IOptions{MigrationOptions}"/> using only the bundled
    /// appsettings.json defaults — no user config file overlaid.
    /// </summary>
    public void LoadFromDefaultsOnly(string appsettingsJson)
    {
        var config = new ConfigurationBuilder()
            .AddJsonStream(ToStream(appsettingsJson))
            .Build();

        ResolvedOptions = BuildOptions(config);
    }

    /// <summary>
    /// Builds an <see cref="IOptions{MigrationOptions}"/> with the given user JSON
    /// overlaid on top of the given appsettings defaults.
    /// </summary>
    public void LoadWithUserConfig(string appsettingsJson, string userConfigJson)
    {
        var config = new ConfigurationBuilder()
            .AddJsonStream(ToStream(appsettingsJson))
            .AddJsonStream(ToStream(userConfigJson))
            .Build();

        ResolvedOptions = BuildOptions(config);
    }

    private static MigrationOptions BuildOptions(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddOptions<MigrationOptions>().Bind(config.GetSection("MigrationPlatform"));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<MigrationOptions>>().Value;
    }

    private static Stream ToStream(string json)
    {
        var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, leaveOpen: true);
        writer.Write(json);
        writer.Flush();
        ms.Position = 0;
        return ms;
    }

    // The real appsettings.json defaults content — mirrors the file in CLI.Migration.
    public const string DefaultsJson = """
        {
          "MigrationPlatform": {
            "ConfigVersion": "1.0",
            "Artefacts": {
              "WorkingDirectory": "%userprofile%\\.DevOpsMigrationPlatform",
              "CreatePackage": false
            },
            "Policies": {
              "Retries": { "Max": 8 },
              "Throttle": { "MaxConcurrency": 4 },
              "Checkpoints": { "Interval": 300 }
            }
          }
        }
        """;
}
