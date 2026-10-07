using Microsoft.Extensions.Logging;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Minimal stderr logger for --debug output. Secrets are never logged:
/// call sites must pass pre-redacted messages only.
/// </summary>
public sealed class DebugLoggerProvider : ILoggerProvider
{
    private readonly bool _enabled;
    private readonly TextWriter _error;

    public DebugLoggerProvider(bool enabled, TextWriter error)
    {
        _enabled = enabled;
        _error = error;
    }

    public ILogger CreateLogger(string categoryName) => new DebugLogger(_enabled, _error);

    public void Dispose()
    {
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }

    private sealed class DebugLogger(bool enabled, TextWriter error) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => enabled;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!enabled)
            {
                return;
            }

            error.WriteLine($"[debug] {formatter(state, exception)}");
            if (exception is not null)
            {
                error.WriteLine(exception.ToString());
            }
        }
    }
}
