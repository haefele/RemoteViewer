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

# Design System (Client)

The client uses a comprehensive design system with markup extensions, design tokens, and reusable components. All UI should use these tokens instead of hardcoded values.

## Markup Extensions

Located in `Themes/` folder. Add `xmlns:theme="using:RemoteViewer.Client.Themes"` to use.

### Spacing (Margin/Padding)
```xml
Padding="{theme:Spacing MD}"              <!-- All sides: 12 -->
Margin="{theme:Spacing X=LG, Y=SM}"       <!-- Horizontal: 16, Vertical: 8 -->
Margin="{theme:Spacing Top=XL, Right=MD}" <!-- Individual sides -->
```

Values: `None=0, XXS=2, XS=4, SM=8, MD=12, LG=16, XL=24, XXL=32`

### Gap (StackPanel/ItemsControl Spacing)
```xml
<StackPanel Spacing="{theme:Gap SM}">     <!-- 8px between items -->
<StackPanel Spacing="{theme:Gap Icon}">   <!-- 6px for icon+text pairs -->
```

Values: `None=0, XXS=2, XS=4, Icon=6, SM=8, MD=12, LG=16, XL=24, XXL=32`

### GridSpacing (Grid Column/Row Definitions)
```xml
<ColumnDefinition Width="{theme:GridSpacing SM}"/>  <!-- 8px spacer column -->
```

## Design Tokens

### Icon Sizes
```xml
Width="{StaticResource IconSizeXS}"   <!-- 12px -->
Width="{StaticResource IconSizeSM}"   <!-- 16px -->
Width="{StaticResource IconSizeMD}"   <!-- 20px -->
Width="{StaticResource IconSizeLG}"   <!-- 24px -->
Width="{StaticResource IconSizeXL}"   <!-- 32px -->
```

### Corner Radii
```xml
CornerRadius="{StaticResource CornerRadiusSM}"      <!-- 4px -->
CornerRadius="{StaticResource CornerRadiusMD}"      <!-- 8px -->
CornerRadius="{StaticResource CornerRadiusLG}"      <!-- 12px -->
CornerRadius="{StaticResource CornerRadiusXL}"      <!-- 20px -->
CornerRadius="{StaticResource CornerRadiusButton}"  <!-- 6px -->
CornerRadius="{StaticResource CornerRadiusCard}"    <!-- 8px -->
```

### Colors (use DynamicResource for theme support)
- **Muted text**: `SystemControlDisabledBaseMediumLowBrush` (Avalonia built-in)
- **Accent**: `AccentButtonBackground`, `AccentButtonForeground` (Avalonia built-in)
- **Surfaces**: `SurfaceElevatedBrush`, `SurfaceOverlayBrush`, `CardBackgroundBrush`
- **Semantic**: `SuccessBrush`, `ErrorBrush`, `WarningBrush`

## Typography Classes

Apply via `Classes="class-name"` on TextBlock:

- **Headings**: `h1` (22px Bold), `h2` (15px SemiBold), `h3` (13px SemiBold)
- **Small text**: `m1` (12px), `m2` (10px)
- **Special**: `credential` (18px Bold monospace)
- **Color modifier**: `muted`

## Button Styles

All buttons automatically get `CornerRadius="6"` from the base style.

```xml
<Button>                             <!-- Default button (uses Avalonia defaults) -->
<Button Classes="accent">            <!-- Accent/primary button (Avalonia built-in) -->
<Button Classes="icon-button">       <!-- 32x32 transparent icon button for toolbars -->
```

## Card Component (Controls/Card.axaml)

A flexible card container with size options.

```xml
<controls:Card>                <!-- Default: 8px radius, 14px padding -->
<controls:Card Size="Large">   <!-- 12px radius, 16px padding -->
<controls:Card Size="XLarge">  <!-- 20px radius, 32x28 padding (for overlays) -->
<controls:Card Size="ListItem"><!-- Uses SurfaceOverlayBrush background -->
```

## Components

- **Card**: Flexible container with size options (`Controls/Card.axaml`)
- **IconBadge**: Circular icon container with sizes Small/Medium/Large (`Controls/IconBadge.axaml`)
- **DialogHeader**: Standardized dialog header with icon, title, subtitle (`Controls/DialogHeader.axaml`)