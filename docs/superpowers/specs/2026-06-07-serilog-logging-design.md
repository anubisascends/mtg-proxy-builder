# Serilog Comprehensive Logging Design

**Date:** 2026-06-07
**Status:** Approved

## Overview

Add structured logging via Serilog to the MTG Proxy Builder application. The app currently has zero structured logging, no global exception handlers, and 39+ silent catch blocks that swallow exceptions. This makes crash diagnosis impossible.

## Decisions

- **Framework:** Serilog with static `Log.Logger` (no DI container changes)
- **Sink:** File only (`%APPDATA%\MTGProxyBuilder\Logs\log-.txt`)
- **Rolling:** Daily, 7-day retention
- **Minimum level:** Information
- **Happy path verbosity:** Moderate (lifecycle + key user actions + API calls)

## Infrastructure

### Packages

| Project | Package | Purpose |
|---------|---------|---------|
| MTGProxyBuilder.UI | Serilog | Core logging |
| MTGProxyBuilder.UI | Serilog.Sinks.File | Rolling file sink |
| MTGProxyBuilder.Core | Serilog | Service-layer logging |

### Initialization (App.xaml.cs)

Configure `Log.Logger` in the App constructor or `OnStartup`:

```
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        path: Path.Combine(appDataDir, "Logs", "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
```

Call `Log.CloseAndFlush()` in `OnExit`.

### Global Exception Handlers (App.xaml.cs)

- `DispatcherUnhandledException`: Log Fatal, show MessageBox with log file path, mark handled
- `AppDomain.CurrentDomain.UnhandledException`: Log Fatal
- `TaskScheduler.UnobservedTaskException`: Log Error, mark observed

## What Gets Logged

### Information (lifecycle + key actions)

- App startup (version, OS)
- App shutdown
- PDF generation start/finish
- Scryfall/MPCFill API search initiated (card name)
- Art selector opened (card name, mode)
- Image download started/completed (source, size)
- Card added/removed from grid
- Library import (count, source)
- Deck import (source, card count)

### Warning

- API non-success status codes
- Image download failed with recovery
- File not found when expected
- Large result set threshold hit (200+)

### Error (replaces all silent catches)

- All 39 silent `catch {}` blocks replaced with `Log.Error(ex, "context")`
- Network failures (Scryfall, MPCFill, Moxfield)
- File I/O failures (cache, library, dock layout, settings)
- Image processing failures

### Fatal

- Global unhandled exceptions (from the three handlers)

## Files Modified

| File | Changes |
|------|---------|
| `MTGProxyBuilder.UI.csproj` | Add Serilog + Serilog.Sinks.File packages |
| `MTGProxyBuilder.Core.csproj` | Add Serilog package |
| `App.xaml.cs` | Logger init, global exception handlers, shutdown flush |
| `MainWindow.xaml.cs` | Log dock layout load/save errors (~2 catches) |
| `ScryfallService.cs` | Log API calls, downloads, errors (~5 catches) |
| `MpcFillService.cs` | Log API calls, downloads, errors (~4 catches) |
| `ImageCacheService.cs` | Replace Debug.WriteLine, log cache ops/errors (~3 catches) |
| `CacheManager.cs` | Log cleanup operations, errors (~5 catches) |
| `ArtLibraryServiceBase.cs` | Log file ops, catalog load/save, errors (~4 catches) |
| `ArtSelectorDialog.xaml.cs` | Log art selector flow, selection, upgrade downloads |
| `MoxfieldService.cs` | Log deck imports, errors (~1 catch) |
| `ThumbnailService.cs` | Log thumbnail errors (~2 catches) |
| `ShellViewModel.cs` | Log update check, errors (~2 catches) |
| `MainViewModel.cs` | Log key actions, errors (~2 catches) |
| `SearchCoordinator.cs` | Log search coordination |

## Files NOT Modified

- Test project (silent catches are cleanup code)
- Models, XAML files, Resources project
- No new files created (besides this design doc)

## Output Template

```
[2026-06-07 14:23:01.234 INF] Art selector opened for "Lightning Bolt" (Front)
[2026-06-07 14:23:01.456 INF] Scryfall search initiated: !"Lightning Bolt"
[2026-06-07 14:23:02.789 INF] Downloaded Scryfall image {CardId} (small, 12KB)
[2026-06-07 14:23:15.012 ERR] Failed to download MPCFill image {Identifier}
System.Net.Http.HttpRequestException: ...
[2026-06-07 14:23:20.345 FTL] Unhandled exception in UI thread
System.NullReferenceException: ...
```
