// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
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

    private const string ModuleName = "PackageConfigStore";

    private readonly IPackageStoreFactory _packageStoreFactory;
    private readonly ILogger<PackageConfigStore> _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly IActiveJobState? _activeJobState;
    private readonly IPackage? _package;

    public PackageConfigStore(
        IPackageStoreFactory packageStoreFactory,
        ILogger<PackageConfigStore> logger,
        IPlatformMetrics? metrics = null,
        IActiveJobState? activeJobState = null,
        IPackage? package = null)
    {
        _packageStoreFactory = packageStoreFactory ?? throw new ArgumentNullException(nameof(packageStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _activeJobState = activeJobState;
        _package = package;
    }

    private MetricsTagList Tags(string operation) =>
        _activeJobState?.JobId is { } jobId
            ? MetricsTagList.Create(jobId, operation, ModuleName)
            : MetricsTagList.Empty;

    /// <inheritdoc />
    public async Task WriteAsync(
        string packageUri,
        string sourceFilePath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageUri)) throw new ArgumentNullException(nameof(packageUri));
        if (string.IsNullOrWhiteSpace(sourceFilePath)) throw new ArgumentNullException(nameof(sourceFilePath));
        if (!File.Exists(sourceFilePath)) throw new FileNotFoundException("Scenario config file not found.", sourceFilePath);

        var (artefactStore, _) = _packageStoreFactory.Create(packageUri);

        using var activity = ActivitySource.StartActivity("config.write");
        activity?.SetTag("operation", "write");
        activity?.SetTag("force", force);

        _logger.LogInformation("Copying config file to package (force={Force})", force);
        var sw = Stopwatch.StartNew();

        try
        {
            var context = new PackageMetaContext(PackageMetaKind.MigrationConfig);
            var exists = _package is not null
                ? await _package.RequestMetaAsync(context, cancellationToken).ConfigureAwait(false) is not null
                : await artefactStore.ExistsAsync(PackagePaths.MigrationConfigFileName, cancellationToken).ConfigureAwait(false);
            if (exists && !force)
            {
                throw new InvalidOperationException(
                    $"{PackagePaths.MigrationConfigFileName} already exists in the package. " +
                    "Re-submit is not permitted without --force.");
            }

#if NET481
            var rawJson = File.ReadAllText(sourceFilePath);
#else
            var rawJson = await File.ReadAllTextAsync(sourceFilePath, cancellationToken)
                .ConfigureAwait(false);
#endif

            if (_package is not null)
            {
                await using var configStream = new MemoryStream(Encoding.UTF8.GetBytes(rawJson), writable: false);
                await _package.PersistMetaAsync(
                    context,
                    new PackageMetaPayload(configStream, "application/json"),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await artefactStore.WriteAsync(PackagePaths.MigrationConfigFileName, rawJson, cancellationToken)
                    .ConfigureAwait(false);
            }

            sw.Stop();
            _metrics?.RecordConfigWriteCompleted(Tags("config.write"));
            activity?.SetTag("outcome", "success");
            _logger.LogInformation("Config copied to package in {DurationMs}ms", sw.ElapsedMilliseconds);
        }
        catch (InvalidOperationException)
        {
            _metrics?.RecordConfigWriteError(Tags("config.write"));
            activity?.SetTag("outcome", "exists_error");
            throw;
        }
        catch (Exception)
        {
            _metrics?.RecordConfigWriteError(Tags("config.write"));
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
        PackageMetaPayload? packagePayload = null;
        int[] backoffMs = { 100, 300, 900 };
        for (var attempt = 0; attempt <= backoffMs.Length; attempt++)
        {
            if (_package is not null)
            {
                packagePayload = await _package.RequestMetaAsync(
                    new PackageMetaContext(PackageMetaKind.MigrationConfig),
                    cancellationToken).ConfigureAwait(false);
                exists = packagePayload is not null;
            }
            else
            {
                exists = await artefactStore.ExistsAsync(PackagePaths.MigrationConfigFileName, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (exists) break;
            if (attempt < backoffMs.Length)
            {
                _logger.LogDebug(
                    "Config not found at '{Path}' on attempt {Attempt}; retrying in {DelayMs}ms",
                    PackagePaths.MigrationConfigFileName, attempt + 1, backoffMs[attempt]);
                await Task.Delay(backoffMs[attempt], cancellationToken).ConfigureAwait(false);
            }
        }

        if (!exists)
        {
            _metrics?.RecordConfigReadError(Tags("config.read"));
            activity?.SetTag("outcome", "not_found");
            _logger.LogWarning(
                "Config not found at '{Path}' after retries. Re-submit the job from the CLI to regenerate it.",
                PackagePaths.MigrationConfigFileName);
            throw new PackageConfigNotFoundException(artefactStore.GetType().Name);
        }

        try
        {
            string? json;
            if (_package is not null)
            {
                packagePayload ??= await _package.RequestMetaAsync(
                    new PackageMetaContext(PackageMetaKind.MigrationConfig),
                    cancellationToken).ConfigureAwait(false);
                if (packagePayload is null)
                    throw new PackageConfigNotFoundException(artefactStore.GetType().Name);

                json = await ReadUtf8Async(packagePayload.Content, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                json = await artefactStore.ReadAsync(PackagePaths.MigrationConfigFileName, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                _metrics?.RecordConfigReadError(Tags("config.read"));
                activity?.SetTag("outcome", "empty");
                _logger.LogError("Config at '{Path}' is empty in package", PackagePaths.MigrationConfigFileName);
                throw new InvalidOperationException(
                    $"{PackagePaths.MigrationConfigFileName} is present but empty. Re-submit the job from the CLI.");
            }

            var bytes = Encoding.UTF8.GetBytes(json);
            using var stream = new MemoryStream(bytes, writable: false);

            var config = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();

            sw.Stop();
            _metrics?.RecordConfigReadCompleted(Tags("config.read"));
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
            _metrics?.RecordConfigReadError(Tags("config.read"));
            activity?.SetTag("outcome", "parse_error");
            _logger.LogError("Failed to parse config at '{Path}'", PackagePaths.MigrationConfigFileName);
            throw;
        }
    }

    private static async Task<string> ReadUtf8Async(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
        cancellationToken.ThrowIfCancellationRequested();
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }
}
