using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;

namespace PJI.DeliveryEventService.SecretsManager.Tests;

public class TestLogger<T> : ILogger<T>
{
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    public ConcurrentBag<(LogLevel Level, string Message)> Messages { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new Mock<IDisposable>().Object;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter.Invoke(state, exception);
        Messages.Add(new(logLevel, message));
    }
}
