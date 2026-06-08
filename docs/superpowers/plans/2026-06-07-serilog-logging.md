# Serilog Comprehensive Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add structured Serilog logging across the entire application to enable crash diagnosis and operational visibility.

**Architecture:** Static `Log.Logger` via Serilog with a rolling file sink. Global exception handlers catch unhandled crashes. All silent catch blocks replaced with logged errors. Key user actions logged at Information level.

**Tech Stack:** Serilog 4.x, Serilog.Sinks.File 6.x, .NET 10, WPF

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj` | Modify | Add Serilog + Serilog.Sinks.File packages |
| `MTGProxyBuilder.Core/MTGProxyBuilder.Core.csproj` | Modify | Add Serilog package |
| `MTGProxyBuilder.UI/App.xaml.cs` | Modify | Logger init, global exception handlers, shutdown flush |
| `MTGProxyBuilder.UI/MainWindow.xaml.cs` | Modify | Log dock layout errors |
| `MTGProxyBuilder.Core/Services/ScryfallService.cs` | Modify | Log API calls, downloads, errors |
| `MTGProxyBuilder.Core/Services/MpcFillService.cs` | Modify | Log API calls, downloads, errors |
| `MTGProxyBuilder.Core/Services/ImageCacheService.cs` | Modify | Replace Debug.WriteLine, log errors |
| `MTGProxyBuilder.Core/Services/CacheManager.cs` | Modify | Log cleanup operations, errors |
| `MTGProxyBuilder.Core/Services/ArtLibraryServiceBase.cs` | Modify | Log catalog load/save, file ops, errors |
| `MTGProxyBuilder.Core/Services/MoxfieldService.cs` | Modify | Log deck fetch, errors |
| `MTGProxyBuilder.UI/Services/ThumbnailService.cs` | Modify | Log thumbnail errors |
| `MTGProxyBuilder.UI/ViewModels/ShellViewModel.cs` | Modify | Log update check, project lifecycle, errors |
| `MTGProxyBuilder.UI/ViewModels/MainViewModel.cs` | Modify | Log key actions, errors |
| `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs` | Modify | Log art selector flow |
| `MTGProxyBuilder.UI/ViewModels/SearchCoordinator.cs` | Modify | Log search coordination |

---

### Task 1: Add NuGet Packages

**Files:**
- Modify: `MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj`
- Modify: `MTGProxyBuilder.Core/MTGProxyBuilder.Core.csproj`

- [ ] **Step 1: Add Serilog packages to UI project**

In `MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj`, add inside the existing `<ItemGroup>` with PackageReferences (after line 10):

```xml
    <PackageReference Include="Serilog" Version="4.2.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
```

- [ ] **Step 2: Add Serilog package to Core project**

In `MTGProxyBuilder.Core/MTGProxyBuilder.Core.csproj`, add a new ItemGroup after line 7:

```xml
  <ItemGroup>
    <PackageReference Include="Serilog" Version="4.2.0" />
  </ItemGroup>
```

- [ ] **Step 3: Restore packages**

Run: `dotnet restore`
Expected: Restore succeeds with no errors.

- [ ] **Step 4: Build to verify**

Run: `dotnet build --no-restore`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj MTGProxyBuilder.Core/MTGProxyBuilder.Core.csproj
git commit -m "chore: add Serilog NuGet packages to UI and Core projects"
```

---

### Task 2: Initialize Serilog and Global Exception Handlers in App.xaml.cs

**Files:**
- Modify: `MTGProxyBuilder.UI/App.xaml.cs`

- [ ] **Step 1: Replace App.xaml.cs with logger init and exception handlers**

Replace the entire content of `MTGProxyBuilder.UI/App.xaml.cs` with:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MTGProxyBuilder.Core.Services;
using Serilog;

namespace MTGProxyBuilder.UI;

public partial class App : Application
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MTGProxyBuilder", "Logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(LogDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev";
        Log.Information("Application starting (v{Version}, {OS})", version, Environment.OSVersion);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");

        try
        {
            var cache = new CacheManager();
            cache.ClearAllCaches();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear caches on exit");
        }

        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI thread exception");
        Log.CloseAndFlush();

        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n" +
            $"Details have been logged to:\n{LogDirectory}",
            "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", e.IsTerminating);
        else
            Log.Fatal("Unhandled AppDomain exception: {ExceptionObject}", e.ExceptionObject);
        Log.CloseAndFlush();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/App.xaml.cs
git commit -m "feat: initialize Serilog with rolling file sink and global exception handlers"
```

---

### Task 3: Add Logging to Core Services — ImageCacheService

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/ImageCacheService.cs`

- [ ] **Step 1: Add using and replace silent catches with Log calls**

Add `using Serilog;` at the top (after the existing `using Newtonsoft.Json;` on line 1).

Replace line 53 (`System.Diagnostics.Debug.WriteLine(...)`) with:
```csharp
                Log.Error(ex, "Failed to cache image from {Url} for {CardId}", imageUrl, cardId);
```

Replace the catch on line 101 (`catch { _metaIndex = new(StringComparer.OrdinalIgnoreCase); }`) with:
```csharp
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load image cache metadata from {Path}", _metadataPath);
                _metaIndex = new(StringComparer.OrdinalIgnoreCase);
            }
```

Replace the catch on line 111 (`catch { }` inside `SaveMetadata`) with:
```csharp
            catch (Exception ex) { Log.Warning(ex, "Failed to save image cache metadata"); }
```

Replace the catch on line 123 (`catch { return false; }` inside `Remove`) with:
```csharp
                catch (Exception ex) { Log.Warning(ex, "Failed to delete cached file {Path}", path); return false; }
```

Replace the catch on line 139 (`catch { /* skip locked files */ }` inside `ClearCache`) with:
```csharp
                    try { File.Delete(file); }
                    catch (Exception ex) { Log.Warning(ex, "Failed to delete cache file {File}", file); }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Core/Services/ImageCacheService.cs
git commit -m "feat: add Serilog logging to ImageCacheService"
```

---

### Task 4: Add Logging to Core Services — CacheManager

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/CacheManager.cs`

- [ ] **Step 1: Add using and replace silent catches**

Add `using Serilog;` at the top of the file.

In `CleanupOnStartup()` — replace the inner catch on line 46 (`try { File.Delete(tmp); } catch { }`) with:
```csharp
                        try { File.Delete(tmp); }
                        catch (Exception ex) { Log.Warning(ex, "Failed to delete temp file {File}", tmp); }
```

Replace the outer catch on line 50 (`catch { }`) with:
```csharp
            catch (Exception ex) { Log.Warning(ex, "Error during startup cache cleanup"); }
```

Add a log at the top of `CleanupOnStartup()` body (after line 33, inside the try):
```csharp
                Log.Information("Running startup cache cleanup");
```

In `ClearDirectory()` — replace the catch on line 109 (`catch { }` inside file delete loop) with:
```csharp
                    catch (Exception ex) { Log.Warning(ex, "Failed to delete file {File}", file); }
```

Replace the catch on line 121 (`catch { }` inside directory delete) with:
```csharp
                    catch (Exception ex) { Log.Warning(ex, "Failed to remove empty directory {Dir}", dir); }
```

Replace the outer catch on line 124 (`catch { }`) with:
```csharp
            catch (Exception ex) { Log.Warning(ex, "Error clearing directory {Path}", path); }
```

In `GetDirectorySize()` — replace the catch on line 137 (`catch { return 0; }`) with:
```csharp
            catch (Exception ex) { Log.Warning(ex, "Failed to calculate size of {Path}", path); return 0; }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Core/Services/CacheManager.cs
git commit -m "feat: add Serilog logging to CacheManager"
```

---

### Task 5: Add Logging to Core Services — ScryfallService

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/ScryfallService.cs`

- [ ] **Step 1: Add using and add logging to API calls and catches**

Add `using Serilog;` at the top (after the existing `using` on line 2).

In `SearchCardAsync()` — add after line 188 (before the `while` loop):
```csharp
                Log.Information("Scryfall search: {Query}", cardName);
```

Replace the catches at lines 215-226:
```csharp
            catch (HttpRequestException ex)
            {
                Log.Error(ex, "Scryfall network error searching for {Query}", cardName);
                return (new(), $"Network error: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                Log.Warning("Scryfall search timed out for {Query}", cardName);
                return (new(), "Request timed out");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Scryfall search failed for {Query}", cardName);
                return (new(), $"Error: {ex.Message}");
            }
```

In `DownloadAndCacheImageAsync()` — add after line 237 (before the return):
```csharp
            Log.Information("Downloading Scryfall image {CardId} ({Size}{Back})", card.Id, size, back ? ", back" : "");
```

In `GetCardByIdAsync()` — replace the catch on line 255 (`catch { return null; }`) with:
```csharp
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch Scryfall card by ID {Id}", scryfallId);
                return null;
            }
```

In `GetCardByNameAsync()` — replace the catch on line 274 (`catch { return null; }`) with:
```csharp
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch Scryfall card by name {Name}", cardName);
                return null;
            }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Core/Services/ScryfallService.cs
git commit -m "feat: add Serilog logging to ScryfallService"
```

---

### Task 6: Add Logging to Core Services — MpcFillService

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/MpcFillService.cs`

- [ ] **Step 1: Add using and add logging**

Add `using Serilog;` at the top of the file.

In `EnsureSourcesLoadedAsync()` — add after line 88 (at start of try):
```csharp
                Log.Information("Loading MPCFill sources");
```

Replace the catches at lines 113-124:
```csharp
            catch (HttpRequestException ex)
            {
                Log.Error(ex, "MPCFill network error loading sources");
                return $"Network error: {ex.Message}";
            }
            catch (TaskCanceledException)
            {
                Log.Warning("MPCFill source loading timed out");
                return "Request timed out.";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load MPCFill sources");
                return $"Error loading sources: {ex.Message}";
            }
```

In `SearchAsync()` — add after line 143 (`var opts = options ?? ...`):
```csharp
                Log.Information("MPCFill search: {Query} (page {PageStart})", query, pageStart);
```

Replace the catches at lines 221-223:
```csharp
            catch (HttpRequestException ex) { Log.Error(ex, "MPCFill network error searching {Query}", query); return (new(), $"Network error: {ex.Message}"); }
            catch (TaskCanceledException) { Log.Warning("MPCFill search timed out for {Query}", query); return (new(), "Request timed out"); }
            catch (Exception ex) { Log.Error(ex, "MPCFill search failed for {Query}", query); return (new(), $"Error: {ex.Message}"); }
```

In `DownloadAndCacheImageAsync()` — add a log before the download (after the `if (string.IsNullOrEmpty(url)) return null;` check):
```csharp
            Log.Information("Downloading MPCFill image {Identifier} ({Mode})", card.Identifier, thumbnail ? "thumbnail" : "full");
```

In `SearchCardbacksAsync()` — replace the catches at lines 372-374:
```csharp
            catch (HttpRequestException ex) { Log.Error(ex, "MPCFill network error fetching cardbacks"); return (new(), $"Network error: {ex.Message}"); }
            catch (TaskCanceledException) { Log.Warning("MPCFill cardback search timed out"); return (new(), "Request timed out"); }
            catch (Exception ex) { Log.Error(ex, "MPCFill cardback search failed"); return (new(), $"Error: {ex.Message}"); }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Core/Services/MpcFillService.cs
git commit -m "feat: add Serilog logging to MpcFillService"
```

---

### Task 7: Add Logging to Core Services — ArtLibraryServiceBase

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/ArtLibraryServiceBase.cs`

- [ ] **Step 1: Add using and replace silent catches**

Add `using Serilog;` at the top (after the existing usings).

In `Remove()` — replace the catch on line 115 (`catch { }`) with:
```csharp
                try { File.Delete(entry.FilePath); }
                catch (Exception ex) { Log.Warning(ex, "Failed to delete library file {Path}", entry.FilePath); }
```

In `MoveToDirectory()` — replace the catch on line 189 (`catch { }` inside existing catalog merge) with:
```csharp
                catch (Exception ex) { Log.Warning(ex, "Failed to parse existing catalog at {Path}", destCatalogPath); }
```

Replace the catch on line 227 (`catch { }` for old directory deletion) with:
```csharp
            catch (Exception ex) { Log.Warning(ex, "Failed to delete old library directory {Dir}", oldDirectory); }
```

In `Load()` — replace line 328 (`System.Diagnostics.Debug.WriteLine(...)`) with:
```csharp
                Log.Error(ex, "Failed to load art library catalog from {Path}", _catalogPath);
```

In `Save()` — replace line 344 (`System.Diagnostics.Debug.WriteLine(...)`) with:
```csharp
                Log.Error(ex, "Failed to save art library catalog to {Path}", _catalogPath);
```

In `ImportFromZip()` — replace the catch on line 286 (`try { File.Delete(tempPath); } catch { }`) with:
```csharp
                        try { File.Delete(tempPath); }
                        catch (Exception ex) { Log.Warning(ex, "Failed to clean up temp file {Path}", tempPath); }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Core/Services/ArtLibraryServiceBase.cs
git commit -m "feat: add Serilog logging to ArtLibraryServiceBase"
```

---

### Task 8: Add Logging to Core Services — MoxfieldService

**Files:**
- Modify: `MTGProxyBuilder.Core/Services/MoxfieldService.cs`

- [ ] **Step 1: Add using and replace silent catches**

Add `using Serilog;` at the top (after existing usings).

In `ParseDeckId()` — replace the catch on line 42 (`catch { }`) with:
```csharp
            catch (Exception ex) { Log.Warning(ex, "Failed to parse Moxfield URL {Url}", url); }
```

In `FetchDeckAsync()` — add after line 53 (start of try):
```csharp
                Log.Information("Fetching Moxfield deck {DeckId}", deckId);
```

In `FetchWithCurlAsync()` — replace the catch on line 133 (`catch { return null; }`) with:
```csharp
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch URL via curl: {Url}", url);
                return null;
            }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.Core/Services/MoxfieldService.cs
git commit -m "feat: add Serilog logging to MoxfieldService"
```

---

### Task 9: Add Logging to UI — ThumbnailService

**Files:**
- Modify: `MTGProxyBuilder.UI/Services/ThumbnailService.cs`

- [ ] **Step 1: Add using and replace silent catches**

Add `using Serilog;` at the top (after the existing usings).

In `Generate()` — replace the catch on line 62 (`catch { return null; }`) with:
```csharp
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to generate thumbnail for {EntryId} from {Path}", entryId, sourceFilePath);
                return null;
            }
```

In `Delete()` — replace the catch on line 72 (`try { File.Delete(path); } catch { }`) with:
```csharp
                try { File.Delete(path); }
                catch (Exception ex) { Log.Warning(ex, "Failed to delete thumbnail {Path}", path); }
```

In `DeleteAll()` — replace the catch on line 80 (`try { File.Delete(file); } catch { }`) with:
```csharp
                try { File.Delete(file); }
                catch (Exception ex) { Log.Warning(ex, "Failed to delete thumbnail file {File}", file); }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/Services/ThumbnailService.cs
git commit -m "feat: add Serilog logging to ThumbnailService"
```

---

### Task 10: Add Logging to UI — MainWindow.xaml.cs

**Files:**
- Modify: `MTGProxyBuilder.UI/MainWindow.xaml.cs`

- [ ] **Step 1: Add using and replace silent catches**

Add `using Serilog;` at the top (after the existing usings).

In `LoadDockLayout()` — replace the catch block at lines 306-309:
```csharp
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load dock layout from {Path}, resetting", DockLayoutPath);
            try { if (File.Exists(DockLayoutPath)) File.Delete(DockLayoutPath); }
            catch (Exception deleteEx) { Log.Warning(deleteEx, "Failed to delete corrupt dock layout file"); }
        }
```

In `SaveDockLayout()` — replace the catch on line 321 (`catch { }`) with:
```csharp
        catch (Exception ex) { Log.Warning(ex, "Failed to save dock layout"); }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/MainWindow.xaml.cs
git commit -m "feat: add Serilog logging to MainWindow dock layout persistence"
```

---

### Task 11: Add Logging to UI — ShellViewModel

**Files:**
- Modify: `MTGProxyBuilder.UI/ViewModels/ShellViewModel.cs`

- [ ] **Step 1: Add using and add logging to key actions and silent catches**

Add `using Serilog;` at the top (after the existing usings).

In constructor, after `_ = CheckForUpdateAsync();` (line 55), add:
```csharp
            Log.Information("ShellViewModel initialized");
```

In `NewProject()` — add at start of method (after line 118):
```csharp
            Log.Information("Creating new project");
```

In `OpenProject()` — add after `dialog.ShowDialog` check (after line 151):
```csharp
            Log.Information("Opening project {Path}", dialog.FileName);
```

In `CheckForUpdateAsync()` — replace the catch on line 320 (`catch { }`) with:
```csharp
            catch (Exception ex) { Log.Warning(ex, "Update check failed"); }
```

In `DownloadUpdate()` — replace the catch on line 335 (`catch { }`) with:
```csharp
                catch (Exception ex) { Log.Warning(ex, "Failed to open update URL {Url}", UpdateDownloadUrl); }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/ViewModels/ShellViewModel.cs
git commit -m "feat: add Serilog logging to ShellViewModel"
```

---

### Task 12: Add Logging to UI — MainViewModel

**Files:**
- Modify: `MTGProxyBuilder.UI/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add using**

Add `using Serilog;` at the top (after the existing usings).

- [ ] **Step 2: Add logging to key user actions**

In `AddScryfallCard()` — add after `SetBusy(...)` (line 1416):
```csharp
                Log.Information("Adding Scryfall card {Name} ({Set})", SelectedScryfallCard.Name, SelectedScryfallCard.SetName);
```

In `ExportPdf()` — add after `SetBusy("Generating PDF...");` (line 1454):
```csharp
            Log.Information("Exporting PDF to {Path} ({CardCount} cards)", dialog.FileName, Cards.Count);
```

In `ImportDeck()` — add after `SetBusy(...)` (line 1862):
```csharp
            Log.Information("Importing deck from {Url} (source: {Source})", ImportDeckUrl, sourceName);
```

In `ShowArtSelector()` — add at the start of the method (after line 1236):
```csharp
            Log.Information("Art selector opened for {CardName} ({Mode})", card.Name, initialMode);
```

In `ImportMpcFillXml()` — add after `SetBusy(...)` (line 1790):
```csharp
            Log.Information("Importing MPCFill XML from {Path}", dialog.FileName);
```

- [ ] **Step 3: Replace silent catches with logging**

In `CheckForUpdateAsync()` — replace the catch on line 278 (`catch { }`) with:
```csharp
            catch (Exception ex) { Log.Warning(ex, "Update check failed"); }
```

In `DownloadUpdate()` — replace the catch on line 317 (`catch { }`) with:
```csharp
                catch (Exception ex) { Log.Warning(ex, "Failed to open update URL {Url}", UpdateDownloadUrl); }
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add MTGProxyBuilder.UI/ViewModels/MainViewModel.cs
git commit -m "feat: add Serilog logging to MainViewModel"
```

---

### Task 13: Add Logging to UI — ArtSelectorDialog

**Files:**
- Modify: `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs`

- [ ] **Step 1: Add using and add logging to key flow points**

Add `using Serilog;` at the top (after the existing usings).

In the constructor, after line 96 (`TitleLabel.Text = "Select Artwork";`), add:
```csharp
            Log.Information("Art selector dialog opened for {CardName} ({Mode})", card.Name, initialMode);
```

In `LoadFrontOptions()` — add after line 342 (`StatusLabel.Text = ...`):
```csharp
            Log.Information("Loading front art options for {CardName}", _card.Name);
```

In `LoadFrontOptions()` — replace the catch inside `scryfallTask` (line 348, `catch { return new List<ScryfallCard>(); }`) with:
```csharp
                catch (Exception ex) { Log.Warning(ex, "Scryfall search failed in art selector"); return new List<ScryfallCard>(); }
```

In `LoadBackOptionsAsync()` — replace the catch on line 491 (`catch { /* Scryfall unavailable — continue with library options */ }`) with:
```csharp
                catch (Exception ex) { Log.Warning(ex, "Scryfall back face lookup failed for {CardName}", _card.Name); }
```

In `OkClick()` — add after the method begins (after `ResultPath = _activeTab.ResultPath;` on line 1062):
```csharp
            Log.Information("Art selected: {Mode} for {Path}", ResultMode, ResultPath);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs
git commit -m "feat: add Serilog logging to ArtSelectorDialog"
```

---

### Task 14: Add Logging to UI — SearchCoordinator

**Files:**
- Modify: `MTGProxyBuilder.UI/ViewModels/SearchCoordinator.cs`

- [ ] **Step 1: Add using and add logging**

Add `using Serilog;` at the top (after the existing usings).

In `SearchScryfallAsync()` — add at the start of the method:
```csharp
            Log.Information("SearchCoordinator: Scryfall search for {Query}", query);
```

In `SearchMpcFillAsync()` — add at the start of the method:
```csharp
            Log.Information("SearchCoordinator: MPCFill search for {Query} (minDpi={MinDpi}, fuzzy={Fuzzy})", query, minDpi, fuzzySearch);
```

In `DownloadScryfallArtAsync()` — add at the start of the method:
```csharp
            Log.Information("Downloading Scryfall art for {CardName} (back={Back})", card.Name, back);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/ViewModels/SearchCoordinator.cs
git commit -m "feat: add Serilog logging to SearchCoordinator"
```

---

### Task 15: Final Build and Smoke Test

- [ ] **Step 1: Full rebuild**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run tests**

Run: `dotnet test`
Expected: All tests pass (Serilog static logger is safe to call without init — returns no-op sink).

- [ ] **Step 3: Commit the design doc and plan**

```bash
git add docs/
git commit -m "docs: add Serilog logging design spec and implementation plan"
```
