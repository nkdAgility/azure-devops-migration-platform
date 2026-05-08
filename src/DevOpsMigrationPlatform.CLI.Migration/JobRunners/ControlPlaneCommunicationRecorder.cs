// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Globalization;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.CLI.JobRunners;

/// <summary>
/// Writes raw control-plane JSON payloads to the CLI diagnostics folder so operators can
/// inspect bootstrap, telemetry, progress, and diagnostics responses exactly as received.
/// </summary>
public sealed class ControlPlaneCommunicationRecorder
{
    private static readonly string[] _diagnosticCategoryPrefixesToPersist =
    [
        "DevOpsMigrationPlatform.",
    ];

    private readonly string _inboxPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastTimestampTicks;

    public ControlPlaneCommunicationRecorder(string diagnosticsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsPath);

        _inboxPath = Path.Combine(diagnosticsPath, "inbox");
        Directory.CreateDirectory(_inboxPath);
    }

    public async Task RecordJsonAsync(string kind, string json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(json))
            return;

        await RecordJsonFileAsync(kind, json, ct).ConfigureAwait(false);
    }

    public Task RecordProgressAsync(ProgressEvent progressEvent, string json, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(progressEvent);

        var module = SanitizeFileComponent(progressEvent.Module, "unknown-module");
        var stage = SanitizeFileComponent(progressEvent.Stage, "unknown-stage");
        return RecordJsonFileAsync($"progress-{module}-{stage}", json, ct);
    }

    public Task RecordDiagnosticAsync(DiagnosticLogRecord diagnostic, string json, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        return ShouldPersistDiagnostic(diagnostic.Category)
            ? RecordJsonFileAsync("diagnostics", json, ct)
            : Task.CompletedTask;
    }

    private async Task RecordJsonFileAsync(string kind, string json, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(json))
            return;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var timestamp = NextTimestamp();
            var fileName = string.Create(
                CultureInfo.InvariantCulture,
                $"{timestamp:yyyyMMdd-HHmmss-fffffff}-{kind}.json");
            var path = Path.Combine(_inboxPath, fileName);
            await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool ShouldPersistDiagnostic(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return false;

        foreach (var prefix in _diagnosticCategoryPrefixesToPersist)
        {
            if (category.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string SanitizeFileComponent(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        var pendingSeparator = false;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && length > 0)
                    buffer[length++] = '-';

                buffer[length++] = char.ToLowerInvariant(character);
                pendingSeparator = false;
                continue;
            }

            pendingSeparator = length > 0;
        }

        if (length == 0)
            return fallback;

        return new string(buffer[..length]);
    }

    private DateTime NextTimestamp()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks <= _lastTimestampTicks)
            nowTicks = _lastTimestampTicks + 1;

        _lastTimestampTicks = nowTicks;
        return new DateTime(nowTicks, DateTimeKind.Utc);
    }
}