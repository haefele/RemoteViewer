namespace RemoteViewer.IntegrationTests.Fixtures;

public static class TestHelpers
{
    public static async Task<T> WaitForEventAsync<T>(
        Action<Action<T>> subscribe,
        TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<T>();
        subscribe(value => tcs.TrySetResult(value));

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        cts.Token.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    public static async Task WaitForEventAsync(
        Action<Action> subscribe,
        TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource();
        subscribe(() => tcs.TrySetResult());

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        cts.Token.Register(() => tcs.TrySetCanceled());
        await tcs.Task;
    }
}
