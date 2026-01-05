using System.Collections.Concurrent;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.IntegrationTests.Mocks;

public class NullInputInjectionService : IInputInjectionService
{
    public ConcurrentBag<(float X, float Y)> MouseMoves { get; } = new();
    public ConcurrentBag<(float X, float Y, MouseButton Button, bool IsDown)> MouseButtons { get; } = new();
    public ConcurrentBag<(float DeltaX, float DeltaY)> MouseWheels { get; } = new();
    public ConcurrentBag<(ushort KeyCode, bool IsDown)> KeyPresses { get; } = new();

    public Task InjectMouseMove(DisplayInfo display, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct)
    {
        this.MouseMoves.Add((normalizedX, normalizedY));
        return Task.CompletedTask;
    }

    public Task InjectMouseButton(DisplayInfo display, MouseButton button, bool isDown, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct)
    {
        this.MouseButtons.Add((normalizedX, normalizedY, button, isDown));
        return Task.CompletedTask;
    }

    public Task InjectMouseWheel(DisplayInfo display, float deltaX, float deltaY, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct)
    {
        this.MouseWheels.Add((deltaX, deltaY));
        return Task.CompletedTask;
    }

    public Task InjectKey(ushort keyCode, bool isDown, string? connectionId, CancellationToken ct)
    {
        this.KeyPresses.Add((keyCode, isDown));
        return Task.CompletedTask;
    }

    public Task ReleaseAllModifiers(string? connectionId, CancellationToken ct)
        => Task.CompletedTask;
}
