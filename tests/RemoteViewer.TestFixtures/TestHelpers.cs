namespace RemoteViewer.TestFixtures;

public static class TestHelpers
{
    /// <summary>
    /// Polls a condition until it returns true or timeout is reached.
    /// </summary>
    /// <param name="condition">The condition to check.</param>
    /// <param name="timeout">Maximum time to wait. Defaults to 30 seconds.</param>
    /// <param name="pollInterval">Time between checks. Defaults to 50ms.</param>
    /// <param name="message">Optional message for timeout exception.</param>
    /// <returns>A task that completes when the condition is true.</returns>
    /// <exception cref="TimeoutException">Thrown if the condition is not met within the timeout.</exception>
    public static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? message = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var effectivePollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(effectivePollInterval);
        }

        throw new TimeoutException(message ?? $"Condition was not met within {effectiveTimeout.TotalSeconds} seconds.");
    }

    /// <summary>
    /// Polls an async condition until it returns true or timeout is reached.
    /// </summary>
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        string? message = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var effectivePollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(effectivePollInterval);
        }

        throw new TimeoutException(message ?? $"Condition was not met within {effectiveTimeout.TotalSeconds} seconds.");
    }
}
