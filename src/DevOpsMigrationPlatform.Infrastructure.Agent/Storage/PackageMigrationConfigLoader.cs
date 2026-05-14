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
/// Package-boundary implementation of <see cref="IPackageMigrationConfigLoader"/>.
/// Loads <c>migration-config.json</c> from the package root through
/// <see cref="IPackageAccess"/>. Compatible with both .NET 10 and net481.
/// </summary>
internal sealed class PackageMigrationConfigLoader : IPackageMigrationConfigLoader
{
    private static readonly ActivitySource ActivitySource =
        new(WellKnownActivitySourceNames.Migration);

    private const string ModuleName = "PackageMigrationConfigLoader";

    private readonly ILogger<PackageMigrationConfigLoader> _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly IActiveJobState? _activeJobState;
    private readonly IPackageAccess _package;

    public PackageMigrationConfigLoader(
        ILogger<PackageMigrationConfigLoader> logger,
        IPackageAccess package,
        IPlatformMetrics? metrics = null,
        IActiveJobState? activeJobState = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _metrics = metrics;
        _activeJobState = activeJobState;
    }

    private MetricsTagList Tags(string operation) =>
        _activeJobState?.JobId is { } jobId
            ? MetricsTagList.Create(jobId, operation, ModuleName)
            : MetricsTagList.Empty;

    /// <inheritdoc />
    public async Task<IConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var activity = ActivitySource.StartActivity("config.read");
        activity?.SetTag("operation", "read");

        var sw = Stopwatch.StartNew();

        PackageMetaResult? metaResult = null;
        int[] backoffMs = { 100, 300, 900 };
        for (var attempt = 0; attempt <= backoffMs.Length; attempt++)
        {
            metaResult = await _package.RequestMetaAsync(
                new PackageMetaContext(PackageMetaKind.MigrationConfig),
                cancellationToken).ConfigureAwait(false);
            if (attempt == 0)
                _logger.LogInformation("Reading config from package via {Path}", metaResult.ResolvedPath);
            if (metaResult.Payload is not null)
                break;
            if (attempt < backoffMs.Length)
            {
                _logger.LogDebug(
                    "Config not found at '{Path}' on attempt {Attempt}; retrying in {DelayMs}ms",
                    metaResult.ResolvedPath, attempt + 1, backoffMs[attempt]);
                await Task.Delay(backoffMs[attempt], cancellationToken).ConfigureAwait(false);
            }
        }

        if (metaResult?.Payload is null)
        {
            _metrics?.RecordConfigReadError(Tags("config.read"));
            activity?.SetTag("outcome", "not_found");
            _logger.LogWarning(
                "Config not found at '{Path}' after retries. Re-submit the job from the CLI to regenerate it.",
                metaResult?.ResolvedPath ?? ".migration/migration-config.json");
            throw new PackageConfigNotFoundException("active package");
        }

        try
        {
            var packagePayload = metaResult.Payload;
            var resolvedPath = metaResult.ResolvedPath;
            var json = await ReadUtf8Async(packagePayload.Content, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
            {
                _metrics?.RecordConfigReadError(Tags("config.read"));
                activity?.SetTag("outcome", "empty");
                _logger.LogError("Config at '{Path}' is empty in package", resolvedPath);
                throw new InvalidOperationException(
                    $"{resolvedPath} is present but empty. Re-submit the job from the CLI.");
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
            _logger.LogError("Failed to parse config at '{Path}'", metaResult?.ResolvedPath ?? ".migration/migration-config.json");
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
