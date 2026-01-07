# Project Overview

RemoteViewer is a .NET 10.0 remote desktop viewer application for Windows. It consists of three main components:

- **RemoteViewer.Client**: Avalonia 11.3-based desktop UI application with MVVM architecture
- **RemoteViewer.Server**: ASP.NET Core SignalR hub server for connection routing
- **RemoteViewer.Shared**: Shared models, protocol messages, and utilities

# Build Commands

```bash
dotnet build                                       # Build entire solution
dotnet build -c Release                            # Release build
dotnet run --project src/RemoteViewer.Server       # Run server
dotnet run --project src/RemoteViewer.Client       # Run client
```

# Testing

Uses TUnit testing framework. Four test projects exist:

```bash
dotnet test                            # Run all tests
```

- **RemoteViewer.Client.Tests**: Unit tests for client-side logic (e.g., CredentialParser)
- **RemoteViewer.Server.Tests**: Unit tests for server services and protocol messages
- **RemoteViewer.Shared.Tests**: Unit tests for shared utilities and models
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

# MVVM Architecture (Client)

The RemoteViewer.Client uses **CommunityToolkit.Mvvm v8.4.0** with Avalonia 11.3's compiled bindings.

For detailed MVVM patterns, examples, and best practices, see the `avalonia-mvvm` skill (.claude/skills/avalonia-mvvm.md).

## Quick Reference

- **ViewModels**: Inherit from `ViewModelBase`, use `IViewModelFactory` for creation
- **Properties**: `[ObservableProperty]` on private fields generates public properties
- **Commands**: `[RelayCommand]` on private methods generates `IRelayCommand` properties
- **Dependencies**: `[NotifyPropertyChangedFor]` and `[NotifyCanExecuteChangedFor]`
- **Bindings**: Use `x:DataType` in XAML for compiled bindings
- **DI**: Services registered in `ServiceRegistration.cs`
- **Threading**: Use `IDispatcher` for UI updates from background threads