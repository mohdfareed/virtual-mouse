using System;
using Microsoft.Extensions.Logging;

internal sealed class ConsoleLogger(string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        _ = state;
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= LogLevel.Information;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _ = eventId;
        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        string level = logLevel.ToString().ToUpperInvariant();
        Console.Error.WriteLine($"{level}: {category}: {message}");
        if (exception is not null)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }
}
