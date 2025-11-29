# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RemoteViewer is a .NET 10.0 remote desktop viewer application for Windows. It consists of three main components that communicate via SignalR:

- **RemoteViewer.Client**: Avalonia-based desktop UI application
- **RemoteViewer.Server**: ASP.NET Core SignalR hub server for connection routing
- **RemoteViewer.WinServ**: Windows Service that captures screens and manages user sessions

## Build Commands

```bash
dotnet build                              # Build entire solution
dotnet build -c Release                   # Release build
dotnet run --project src/RemoteViewer.Server      # Run server
dotnet run --project src/RemoteViewer.Client      # Run client
dotnet run --project src/RemoteViewer.WinServ     # Run Windows service
```

## Testing

No traditional unit test framework. Instead, two manual test utilities exist:

```bash
# Test SignalR hub connections and message routing
dotnet run --project tests/RemoteViewer.HubClientTest

# Test screen capture (DXGI/BitBlt) and Windows session management
dotnet run --project tests/RemoteViewer.DesktopDupTest
```

## Architecture

### Communication Flow

```
Client (Avalonia) → SignalR (/connection) → Server → Message Router
                                                   ↓
WinServ (Windows Service) ← Session Recorders ← Screen Capture
```

### Server Core Services

**IConnectionsService** (`src/RemoteViewer.Server/Services/IConnectionsService.cs`):
- Central hub for client registration and connection management
- Generates credentials (10-digit username, 8-char password)
- Routes messages with three modes: `PresenterOnly`, `AllViewers`, `All`
- Thread-safe with `ReaderWriterLockSlim`

**ConnectionHub** (`src/RemoteViewer.Server/Hubs/ConnectionHub.cs`):
- SignalR hub at `/connection` endpoint
- Handles registration, connection requests, and binary message routing

### WinServ Architecture

**Dual-Mode Operation**:
- `WindowsService` mode: Runs as SYSTEM, spawns session recorders per RDP session
- `SessionRecorder` mode: Runs in user session, captures screen

**Screen Capture** (`src/RemoteViewer.WinServ/Services/`):
- `DxgiScreenGrabber`: Primary capture using DXGI Output Duplication (efficient, dirty rect tracking)
- `BitBltScreenGrabber`: Fallback GDI-based capture

**IWin32Service**: Windows API helpers for session enumeration, desktop switching, process creation as user

### Client Architecture

- Avalonia 11.x with MVVM Community Toolkit
- ViewLocator pattern for view/viewmodel mapping
- SignalR client for server communication

## Code Conventions

- Source-generated logging with `[LoggerMessage]` attributes (see `ConnectionsServiceLogs.cs`)
- Records for data transfer objects
- `ImmutableList` and `ReadOnlySet` for thread-safe collections
- Enum-based error codes instead of exceptions for expected failures
- Safe P/Invoke wrappers with error logging

## Key Dependencies

- **SignalR**: Real-time client-server communication
- **Vortice.DXGI**: DirectX screen capture
- **SkiaSharp**: Image processing
- **Serilog**: Structured logging with file and console sinks
- **CsWin32**: Windows API PInvoke bindings
