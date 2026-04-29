using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

/// <summary>
/// File-based implementation of <see cref="IPackageConfigStore"/>.
/// Reads and writes <c>migration-config.json</c> at the package root using
/// <see cref="IArtefactStore"/>. Compatible with both .NET 10 and net481.
/// </summary>
internal sealed class PackageConfigStore : IPackageConfigStore
{
    private static readonly ActivitySource ActivitySource =
        new(WellKnownActivitySourceNames.Migration);

    private readonly ILogger<PackageConfigStore> _logger;
    private readonly IMigrationMetrics? _metrics;

    public PackageConfigStore(ILogger<PackageConfigStore> logger, IMigrationMetrics? metrics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        IArtefactStore artefactStore,
        string sourceFilePath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (artefactStore == null) throw new ArgumentNullException(nameof(artefactStore));
        if (string.IsNullOrWhiteSpace(sourceFilePath)) throw new ArgumentNullException(nameof(sourceFilePath));
        if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Scenario config file not found.", sourceFilePath);

        using var activity = ActivitySource.StartActivity("config.write");
        activity?.SetTag("operation", "write");
        activity?.SetTag("force", force);

        _logger.LogInformation("Copying config file to package (force={Force})", force);
        var sw = Stopwatch.StartNew();

        try
        {
            var exists = await artefactStore.ExistsAsync(PackagePaths.MigrationConfigFileName, cancellationToken)
                .ConfigureAwait(false);
            if (exists && !force)
            {
                throw new InvalidOperationException(
                    "migration-config.json already exists in the package. " +
                    "Re-submit is not permitted without --force.");
            }

#if NET481
            var rawJson = File.ReadAllText(sourceFilePath);
#else
            var rawJson = await File.ReadAllTextAsync(sourceFilePath, cancellationToken)
                .ConfigureAwait(false);
#endif

            await artefactStore.WriteAsync(PackagePaths.MigrationConfigFileName, rawJson, cancellationToken)
                .ConfigureAwait(false);

            sw.Stop();
            var emptyTags = default(TagList);
            _metrics?.RecordConfigWriteCompleted(in emptyTags);
            activity?.SetTag("outcome", "success");
            _logger.LogInformation("Config copied to package in {DurationMs}ms", sw.ElapsedMilliseconds);
        }
        catch (InvalidOperationException)
        {
            var emptyTags = default(TagList);
            _metrics?.RecordConfigWriteError(in emptyTags);
            activity?.SetTag("outcome", "exists_error");
            throw;
        }
        catch (Exception)
        {
            var emptyTags = default(TagList);
            _metrics?.RecordConfigWriteError(in emptyTags);
            activity?.SetTag("outcome", "error");
            _logger.LogError("Failed to copy config to package");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IConfiguration> ReadAsync(
        IArtefactStore artefactStore,
        CancellationToken cancellationToken = default)
    {
        if (artefactStore == null) throw new ArgumentNullException(nameof(artefactStore));

        using var activity = ActivitySource.StartActivity("config.read");
        activity?.SetTag("operation", "read");

        _logger.LogInformation("Reading config from package via {Path}", PackagePaths.MigrationConfigFileName);
        var sw = Stopwatch.StartNew();

        var exists = false;
        int[] backoffMs = { 100, 300, 900 };
        for (var attempt = 0; attempt <= backoffMs.Length; attempt++)
        {
            exists = await artefactStore.ExistsAsync(PackagePaths.MigrationConfigFileName, cancellationToken)
                .ConfigureAwait(false);
            if (exists) break;
            if (attempt < backoffMs.Length)
            {
                _logger.LogDebug(
                    "migration-config.json not found on attempt {Attempt}; retrying in {DelayMs}ms",
                    attempt + 1, backoffMs[attempt]);
                await Task.Delay(backoffMs[attempt], cancellationToken).ConfigureAwait(false);
            }
        }

        if (!exists)
        {
            var emptyTags = default(TagList);
            _metrics?.RecordConfigReadError(in emptyTags);
            _metrics?.RecordConfigReadFallback(in emptyTags);
            activity?.SetTag("outcome", "not_found");
            _logger.LogWarning(
                "migration-config.json not found in package after retries. " +
                "Re-submit the job from the CLI to regenerate it.");
            throw new PackageConfigNotFoundException(artefactStore.GetType().Name);
        }

        try
        {
            var json = await artefactStore.ReadAsync(PackagePaths.MigrationConfigFileName, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
            {
                var emptyTags = default(TagList);
                _metrics?.RecordConfigReadError(in emptyTags);
                activity?.SetTag("outcome", "empty");
                _logger.LogError("migration-config.json is empty in package");
                throw new InvalidOperationException(
                    "migration-config.json is present but empty. Re-submit the job from the CLI.");
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            using var stream = new MemoryStream(bytes, writable: false);

            var config = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();

            sw.Stop();
            var emptyTags2 = default(TagList);
            _metrics?.RecordConfigReadCompleted(in emptyTags2);
            activity?.SetTag("outcome", "success");
            _logger.LogInformation("Config read from package in {DurationMs}ms", sw.ElapsedMilliseconds);

            return config;
        }
        catch (PackageConfigNotFoundException)
        {
            throw;
        }
        catch (Exception)
        {
            var emptyTags = default(TagList);
            _metrics?.RecordConfigReadError(in emptyTags);
            activity?.SetTag("outcome", "parse_error");
            _logger.LogError("Failed to parse migration-config.json");
            throw;
        }
    }
}