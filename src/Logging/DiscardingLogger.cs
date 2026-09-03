using Microsoft.Extensions.Logging;

namespace DotNetPerfPatterns.Logging;

/// <summary>
/// An ILogger wired up the normal way but writing nowhere. Sending the output somewhere would
/// measure the sink; what's being measured here is what the caller pays before the logger is
/// reached.
///
/// Note that "off" here means the level filter drops the provider entirely. In production it
/// more often means the provider is alive and the level simply doesn't pass, which costs a few
/// nanoseconds more per call than measured here.
/// </summary>
internal static class DiscardingLogger
{
    public static (ILogger Logger, ILoggerFactory Factory) Create(bool enabled)
    {
        ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new DiscardingProvider());
            builder.SetMinimumLevel(enabled ? LogLevel.Information : LogLevel.None);
        });

        return (factory.CreateLogger("Benchmarks"), factory);
    }

    private sealed class DiscardingProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Sink();

        public void Dispose()
        {
        }

        private sealed class Sink : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _ = formatter(state, exception).Length;
        }
    }
}
