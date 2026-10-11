using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace EmployeeData.IntegrationTests;

internal sealed class TestLogSink
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public void Clear()
    {
        _messages.Clear();
    }

    public void Write(string message)
    {
        _messages.Enqueue(message);
    }
}

internal sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly TestLogSink _sink;

    public TestLoggerProvider(TestLogSink sink)
    {
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestLogger(_sink);
    }

    public void Dispose()
    {
    }

    private sealed class TestLogger : ILogger
    {
        private readonly TestLogSink _sink;

        public TestLogger(TestLogSink sink)
        {
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _sink.Write(formatter(state, exception));
        }
    }
}
