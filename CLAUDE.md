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
- **Text**: `TextPrimaryBrush`, `TextSecondaryBrush`, `TextMutedBrush`, `TextDisabledBrush`
- **Surfaces**: `SurfaceBrush`, `SurfaceElevatedBrush`, `CardBackgroundBrush`
- **Borders**: `BorderSubtleBrush`, `BorderDefaultBrush`, `BorderStrongBrush`
- **Semantic**: `AccentBrush`, `SuccessBrush`, `ErrorBrush`, `WarningBrush`, `InfoBrush`

## Typography Classes

Apply via `Classes="class-name"` on TextBlock:

- **Titles**: `title-large` (22px Bold), `title` (20px SemiBold)
- **Headers**: `header` (15px SemiBold), `header-small` (13px SemiBold)
- **Body**: `body` (14px), `body-small` (13px)
- **Small**: `caption` (12px), `small` (11px), `small-muted` (10px)
- **Special**: `credential` (18px Bold monospace), `monospace` (13px)
- **Color modifiers**: `secondary`, `muted`, `accent`, `success`, `error`, `warning`

## Button Styles

```xml
<Button Classes="icon-button">       <!-- 32x32 transparent icon button -->
<Button Classes="icon-button-lg">    <!-- 40x40 larger icon button -->
<Button Classes="action-primary">    <!-- Accent background, white text -->
<Button Classes="action-secondary">  <!-- Subtle background -->
<Button Classes="ghost">             <!-- Transparent, text only -->
<Button Classes="danger">            <!-- Error/destructive action -->
<Button Classes="success">           <!-- Positive action -->
```

## Card Component & Styles

### Card Component (Controls/Card.axaml)
```xml
<controls:Card Variant="Default|Elevated|Surface|AccentStrip" Accent="None|Success|Error|Info|Warning|Primary">
    <!-- Content -->
</controls:Card>
```

### Card Utility Classes
```xml
<Border Classes="card">              <!-- Basic card -->
<Border Classes="card-elevated">     <!-- Card with shadow -->
<Border Classes="accent-strip success">  <!-- Left accent border -->
```

## Components

- **Card**: Flexible container with variants (`Controls/Card.axaml`)
- **IconBadge**: Circular icon container with sizes Small/Medium/Large (`Controls/IconBadge.axaml`)
- **DialogHeader**: Standardized dialog header with icon, title, subtitle (`Controls/DialogHeader.axaml`)