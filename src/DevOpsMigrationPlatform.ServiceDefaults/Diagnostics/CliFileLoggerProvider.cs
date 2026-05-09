// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Diagnostics;

/// <summary>
/// A file-based <see cref="ILoggerProvider"/> that only emits log entries for categories
/// that start with <c>DevOpsMigrationPlatform.</c>. All infrastructure noise from
/// <c>System.Net.Http</c>, <c>Microsoft.Hosting</c>, <c>Microsoft.Extensions</c>, etc.
/// is suppressed at the provider level so the output file contains only platform logs.
/// </summary>
internal sealed class CliFileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public CliFileLoggerProvider(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new CliFileLogger(this, categoryName);

    public void Dispose() => _writer.Dispose();

    private sealed class CliFileLogger(CliFileLoggerProvider provider, string categoryName) : ILogger
    {
        private static readonly string[] _allowedPrefixes =
        [
            "DevOpsMigrationPlatform.",
        ];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel < LogLevel.Information)
                return false;

            foreach (var prefix in _allowedPrefixes)
            {
                if (categoryName.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            lock (provider._lock)
            {
                provider._writer.WriteLine($"[{DateTimeOffset.UtcNow:O}] [{logLevel}] {categoryName}: {message}");

                if (exception is not null)
                {
                    provider._writer.WriteLine($"  Exception: {exception}");
                }
            }
        }
    }
}
