using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Diagnostics;

/// <summary>
/// A minimal <see cref="ILoggerProvider"/> that writes structured log entries to a file.
/// Used for diagnostic purposes only — not intended as a production log sink.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;

    public FileLoggerProvider(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_writer, categoryName);

    public void Dispose() => _writer.Dispose();

    private sealed class FileLogger(StreamWriter writer, string categoryName) : ILogger
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
            writer.WriteLine($"[{DateTimeOffset.UtcNow:O}] [{logLevel}] {categoryName}: {message}");

            if (exception is not null)
            {
                writer.WriteLine($"  Exception: {exception}");
            }
        }
    }
}
