using System.IO;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TimesGateMonitor.App.Infrastructure;

internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const long MaximumBytes = 2 * 1024 * 1024;
    private const int RetainedFiles = 4;
    private readonly object _sync = new();
    private readonly string _path;
    private StreamWriter? _writer;
    private bool _disposed;

    public RollingFileLoggerProvider(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "tgm.log");
    }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                EnsureWriter();
                if (_writer?.BaseStream.Length >= MaximumBytes)
                {
                    RollFiles();
                    EnsureWriter();
                }

                var shortCategory = category.Split('.').LastOrDefault() ?? category;
                _writer?.Write(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
                _writer?.Write(" [");
                _writer?.Write(level.ToString().ToUpperInvariant());
                _writer?.Write("] ");
                _writer?.Write(shortCategory);
                _writer?.Write(": ");
                _writer?.WriteLine(message.ReplaceLineEndings(" "));
                if (exception is not null)
                {
                    _writer?.WriteLine(exception.ToString().ReplaceLineEndings(" "));
                }

                _writer?.Flush();
            }
            catch (IOException)
            {
                // Logging must never interrupt monitoring.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never interrupt monitoring.
            }
        }
    }

    private void EnsureWriter()
    {
        if (_writer is not null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void RollFiles()
    {
        _writer?.Dispose();
        _writer = null;

        var oldest = $"{_path}.{RetainedFiles}";
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var index = RetainedFiles - 1; index >= 1; index--)
        {
            var source = $"{_path}.{index}";
            if (File.Exists(source))
            {
                File.Move(source, $"{_path}.{index + 1}", overwrite: true);
            }
        }

        if (File.Exists(_path))
        {
            File.Move(_path, $"{_path}.1", overwrite: true);
        }
    }

    private sealed class RollingFileLogger(
        RollingFileLoggerProvider provider,
        string category) : ILogger
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
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
