using Microsoft.Extensions.Logging;

namespace UGL.App;

/// <summary>
/// Simple file logger that writes all log entries to a text file.
/// Used to capture log output when the fullscreen window covers the console.
/// Log file: {exe}/logs/ugl.log — tail it with:
///   Get-Content logs\ugl.log -Wait -Tail 20
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
        // Overwrite on each run so the file stays manageable
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _writer, _lock);

    public void Dispose() => _writer.Dispose();
}

public sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly StreamWriter _writer;
    private readonly object _lock;

    public FileLogger(string category, StreamWriter writer, object lck)
    {
        _category = category;
        _writer = writer;
        _lock = lck;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Debug;

    public void Log<TState>(LogLevel level, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        var msg = $"{DateTime.Now:HH:mm:ss.fff} [{level.ToString()[..3].ToUpper()}] {_category}: {formatter(state, exception)}";
        if (exception is not null) msg += $"\n  {exception}";
        lock (_lock) { _writer.WriteLine(msg); }
    }
}
