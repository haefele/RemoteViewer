namespace RemoteViewer.TestFixtures;

public static class TestHelpers
{
    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_defaultPollInterval = TimeSpan.FromMilliseconds(50);

    public static async Task<T> WaitForEvent<T>(Action<Action<T>> subscribe)
    {
        var tcs = new TaskCompletionSource<T>();
        subscribe(value => tcs.TrySetResult(value));

        using var cts = new CancellationTokenSource(s_defaultTimeout);
        cts.Token.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    public static async Task WaitForEvent(Action<Action> subscribe)
    {
        var tcs = new TaskCompletionSource();
        subscribe(() => tcs.TrySetResult());

        using var cts = new CancellationTokenSource(s_defaultTimeout);
        cts.Token.Register(() => tcs.TrySetCanceled());
        await tcs.Task;
    }

    public static async Task WaitForReceivedCall(Func<bool> checkReceived)
    {
        await WaitUntil(checkReceived, message: "WaitForReceivedCall timed out");
    }

    public static async Task WaitUntil(Func<bool> condition, string? message = null)
    {
        var deadline = DateTime.UtcNow + s_defaultTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(s_defaultPollInterval);
        }

        throw new TimeoutException(message ?? $"Condition was not met within {s_defaultTimeout.TotalSeconds} seconds.");
    }

    public static async Task WaitUntil(Func<Task<bool>> condition, string? message = null)
    {
        var deadline = DateTime.UtcNow + s_defaultTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(s_defaultPollInterval);
        }

        throw new TimeoutException(message ?? $"Condition was not met within {s_defaultTimeout.TotalSeconds} seconds.");
    }
}
