// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Globalization;
using System.Threading;

namespace DevOpsMigrationPlatform.CLI.JobRunners;

/// <summary>
/// Writes raw control-plane JSON payloads to the CLI diagnostics folder so operators can
/// inspect bootstrap, telemetry, progress, and diagnostics responses exactly as received.
/// </summary>
public sealed class ControlPlaneCommunicationRecorder
{
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

    private DateTime NextTimestamp()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        if (nowTicks <= _lastTimestampTicks)
            nowTicks = _lastTimestampTicks + 1;

        _lastTimestampTicks = nowTicks;
        return new DateTime(nowTicks, DateTimeKind.Utc);
    }
}