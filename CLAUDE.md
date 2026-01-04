# Project Overview

RemoteViewer is a .NET 10.0 remote desktop viewer application for Windows. It consists of three main components:

- **RemoteViewer.Client**: Avalonia-based desktop UI application
- **RemoteViewer.Server**: ASP.NET Core SignalR hub server for connection routing
- **RemoteViewer.WinServ**: Windows Service that enables unattended remote access

# Build Commands

```bash
dotnet build                              # Build entire solution
dotnet build -c Release                   # Release build
dotnet run --project src/RemoteViewer.Server      # Run server
dotnet run --project src/RemoteViewer.Client      # Run client
dotnet run --project src/RemoteViewer.WinServ     # Run Windows service
```

# Testing

Uses TUnit testing framework. Three test projects exist:

```bash
dotnet test                            # Run all tests
```

- **RemoteViewer.Client.Tests**: Unit tests for client-side logic (e.g., CredentialParser)
- **RemoteViewer.Server.Tests**: Unit tests for server services and protocol messages
- **RemoteViewer.IntegrationTests**: End-to-end SignalR hub integration tests

# Code Conventions

## Logging
- Source-generated logging with `[LoggerMessage]` attributes in separate `*Logs.cs` files
- Structured logging with named parameters for context

## Error Handling
- Nullable enum return types for expected failures (e.g., `Task<TryConnectError?>`)
- Exceptions only for unexpected/startup errors; graceful `false`/`null` returns otherwise

## P/Invoke
- CsWin32 source-generated bindings with SafeHandle wrappers
- `Marshal.GetLastWin32Error()` for error logging

## Documentation
- No XML docs (`///`) - code should be self-explanatory
- You can add comments if necessary for clarity