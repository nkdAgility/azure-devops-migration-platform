using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Diagnostics;

/// <summary>
/// A minimal <see cref="ILoggerProvider"/> that writes structured log entries to a file.
/// Used for diagnostic purposes only — not intended as a production log sink.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public FileLoggerProvider(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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
