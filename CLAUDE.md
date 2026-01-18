# RemoteViewer

A .NET 10 remote desktop viewer for Windows with three components:

- **RemoteViewer.Client**: Avalonia 11.3 desktop UI with MVVM architecture
- **RemoteViewer.Server**: ASP.NET Core SignalR hub for connection routing
- **RemoteViewer.Shared**: Shared models, protocol messages, and utilities

## Build & Run

```bash
dotnet build                                       # Build entire solution
dotnet run --project src/RemoteViewer.Server       # Run server
dotnet run --project src/RemoteViewer.Client       # Run client
dotnet test                                        # Run all tests (TUnit)
```

## Code Conventions

- **Logging**: Source-generated using `[LoggerMessage]` attributes, preferable in the same file where they are used
- **Error handling**: Nullable enum returns for expected failures (e.g., `Task<TryConnectError?>`); exceptions only for unexpected errors
- **P/Invoke**: CsWin32 source-generated bindings with SafeHandle wrappers
- **No XML docs**: Code should be self-explanatory; add comments only where necessary

