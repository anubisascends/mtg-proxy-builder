# Progressive Art Selector Loading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure ArtSelectorDialog to show placeholder tiles immediately and stream thumbnail images in progressively as they download.

**Architecture:** Split `LoadFrontOptions` into two phases: Phase 1 creates all placeholder tiles from API search results using the existing `ArtTileBuilder.CreateDeferredTile`. Phase 2 fires off concurrent thumbnail downloads and swaps images into tiles as each completes via `Dispatcher.BeginInvoke`. The 200+ result confirmation dialog is removed.

**Tech Stack:** WPF, C#, existing ArtTileBuilder, SemaphoreSlim for download concurrency

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs` | Modify | Restructure `LoadFrontOptions` and `LoadBackOptionsAsync` to two-phase progressive loading |
| `MTGProxyBuilder.UI/Controls/ArtTileBuilder.cs` | Modify (minor) | Add `CreatePlaceholderTile` method for tiles with no image path yet |

---

### Task 1: Add CreatePlaceholderTile to ArtTileBuilder

**Files:**
- Modify: `MTGProxyBuilder.UI/Controls/ArtTileBuilder.cs`

The existing `CreateDeferredTile` already creates a tile with an empty Image control, but it shows a black rectangle. We need a variant that shows a "Loading..." indicator instead so the user knows images are coming.

- [ ] **Step 1: Add CreatePlaceholderTile method**

In `MTGProxyBuilder.UI/Controls/ArtTileBuilder.cs`, add this method after the existing `CreateDeferredTile` method (after line 133):

```csharp
        /// <summary>Creates a placeholder tile with a "Loading..." indicator and empty Image for later assignment.</summary>
        public static (Border Border, Image ImageControl) CreatePlaceholderTile(string label, string detail,
            string? mpcSource = null)
        {
            var border = new Border
            {
                Width = TileWidth, Height = TileHeight, Margin = new Thickness(4),
                Background = AppBrushes.TileBg,
                CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                ToolTip = $"{label}\n{detail}"
            };

            var stack = new StackPanel();

            var imgBorder = new Border
            {
                Height = ImageHeight, Background = Brushes.Black,
                CornerRadius = new CornerRadius(3, 3, 0, 0), ClipToBounds = true
            };
            var grid = new System.Windows.Controls.Grid();
            var loadingText = new TextBlock
            {
                Text = "Loading...", Foreground = Brushes.Gray, FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(loadingText);
            var img = new Image { Stretch = Stretch.UniformToFill };
            grid.Children.Add(img);
            imgBorder.Child = grid;
            stack.Children.Add(imgBorder);

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = AppBrushes.TextSecondary,
                FontSize = LabelFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 4, 3, 0)
            };
            stack.Children.Add(lbl);

            var detailLbl = new TextBlock
            {
                Text = detail, Foreground = AppBrushes.TextMuted,
                FontSize = DetailFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(3, 0, 3, 2)
            };
            stack.Children.Add(detailLbl);

            border.Child = stack;
            return (border, img);
        }
```

The `Image` control sits on top of the "Loading..." text in a Grid. When the image loads, it covers the text naturally. If the download fails, "Loading..." remains visible (acting as the "no image" indicator).

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/Controls/ArtTileBuilder.cs
git commit -m "feat: add CreatePlaceholderTile to ArtTileBuilder for progressive loading"
```

---

### Task 2: Restructure LoadFrontOptions — Phase 1 (placeholder tiles)

**Files:**
- Modify: `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs`

This task replaces everything in `LoadFrontOptions` from the "// 2. Kick off API searches concurrently" comment through the end of the method. The library section (section 1) stays exactly as-is.

- [ ] **Step 1: Replace the download-then-add-tiles section**

In `ArtSelectorDialog.xaml.cs`, find the `LoadFrontOptions` method. Keep everything from the start through the library loading section (lines 232-342, ending after the library thumbnail loading). Replace everything from line 344 (`// 2. Kick off API searches concurrently`) through line 463 (end of method, just before `LoadBackOptionsAsync`) with:

```csharp
            // 2. Kick off API searches concurrently
            StatusLabel.Text = $"Searching for \"{_card.Name}\"...";
            Log.Information("Loading front art options for {CardName}", _card.Name);
            var mpcOpts = BuildSearchOptionsFromControls();
            mpcOpts.FuzzySearch = false;
            var scryfallTask = Task.Run(async () =>
            {
                try { return (await _scryfall.SearchCardAsync($"!\"{_card.Name}\"")).Cards; }
                catch (Exception ex) { Log.Warning(ex, "Scryfall search failed in art selector"); return new List<ScryfallCard>(); }
            });
            var mpcTask = Task.Run(async () =>
            {
                try
                {
                    var (results, _) = await _mpcFill.SearchAsync(
                        _card.Name, fuzzySearch: false, sourcesOverride: _mpcSourcesOverride,
                        options: mpcOpts);
                    return results
                        .Where(mc => mc.Name.Contains(_card.Name, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
                catch { return new List<MpcFillCard>(); }
            });

            await Task.WhenAll(scryfallTask, mpcTask);
            var scryfallResults = scryfallTask.Result;
            var mpcResults = mpcTask.Result;

            // Skip MPCFill results that are already in the local library
            if (libraryNames.Count > 0)
                mpcResults = mpcResults
                    .Where(mc => !libraryNames.Contains($"{mc.Name} [{mc.Source}]"))
                    .ToList();

            int totalImages = scryfallResults.Count + mpcResults.Count;
            StatusLabel.Text = $"Found {totalImages} result(s), downloading thumbnails...";

            // Phase 1: Create all placeholder tiles immediately
            var scryfallTiles = new List<(Image img, ScryfallCard card, string label, string detail)>();
            foreach (var sc in scryfallResults.Where(sc => sc.GetImageUrl() != null))
            {
                string label = $"{sc.SetName} #{sc.CollectorNumber}";
                string detail = $"Scryfall | {sc.Artist ?? ""}";
                var (border, img) = Controls.ArtTileBuilder.CreatePlaceholderTile(label, detail);

                // Wire up click handlers with a mutable path reference
                string capturedLabel = label;
                string capturedDetail = detail;
                string? tilePath = null;
                border.MouseLeftButtonUp += (_, _) =>
                {
                    if (tilePath != null) SelectOption(tab, capturedLabel, tilePath, capturedDetail, border);
                };
                border.MouseLeftButtonDown += (_, ev) =>
                {
                    if (ev.ClickCount == 2 && tilePath != null)
                    {
                        SelectOption(tab, capturedLabel, tilePath, capturedDetail, border);
                        OkClick(null!, null!);
                    }
                };
                border.MouseRightButtonUp += (_, ev) =>
                {
                    var menu = new System.Windows.Controls.ContextMenu();
                    var previewItem = new System.Windows.Controls.MenuItem { Header = "Preview Full Size" };
                    previewItem.Click += (_, _) =>
                    {
                        if (tilePath != null)
                        {
                            var preview = new ImagePreviewDialog(tilePath, capturedLabel);
                            preview.Owner = this;
                            preview.ShowDialog();
                        }
                    };
                    menu.Items.Add(previewItem);
                    menu.IsOpen = true;
                    ev.Handled = true;
                };

                tab.OptionsPanel.Children.Add(border);
                tab.AllTiles.Add(new TileInfo(border, label, "Scryfall", detail));
                scryfallTiles.Add((img, sc, label, detail));
            }

            var mpcTiles = new List<(Image img, MpcFillCard card, string label, string detail, string mpcSource)>();
            foreach (var mc in mpcResults)
            {
                string label = mc.Name;
                string detail = $"MPCFill | {mc.Source} | {mc.Dpi} DPI";
                string mpcSource = mc.Source;
                var (border, img) = Controls.ArtTileBuilder.CreatePlaceholderTile(label, detail);

                string capturedLabel = label;
                string capturedDetail = detail;
                string capturedMpcSource = mpcSource;
                string? tilePath = null;
                border.MouseLeftButtonUp += (_, _) =>
                {
                    if (tilePath != null) SelectOption(tab, capturedLabel, tilePath, capturedDetail, border);
                };
                border.MouseLeftButtonDown += (_, ev) =>
                {
                    if (ev.ClickCount == 2 && tilePath != null)
                    {
                        SelectOption(tab, capturedLabel, tilePath, capturedDetail, border);
                        OkClick(null!, null!);
                    }
                };
                border.MouseRightButtonUp += (_, ev) =>
                {
                    var menu = new System.Windows.Controls.ContextMenu();
                    var previewItem = new System.Windows.Controls.MenuItem { Header = "Preview Full Size" };
                    previewItem.Click += (_, _) =>
                    {
                        if (tilePath != null)
                        {
                            var preview = new ImagePreviewDialog(tilePath, capturedLabel);
                            preview.Owner = this;
                            preview.ShowDialog();
                        }
                    };
                    menu.Items.Add(previewItem);

                    if (_frontArtLibrary != null)
                    {
                        var saveItem = new System.Windows.Controls.MenuItem { Header = "Save to Library" };
                        saveItem.Click += async (_, _) =>
                        {
                            string savePath = tilePath ?? "";
                            if (tilePath != null && tab.MpcFillCardsByPath.TryGetValue(tilePath, out var mpcCard))
                            {
                                StatusLabel.Text = "Downloading full resolution...";
                                var fullPath = await _mpcFill.DownloadAndCacheImageAsync(mpcCard);
                                if (fullPath != null) savePath = fullPath;
                            }
                            string libName = $"{capturedLabel} [{capturedMpcSource}]";
                            var entry = _frontArtLibrary.AddFromFile(savePath, libName, capturedMpcSource);
                            if (entry != null)
                            {
                                var sc2 = tab.ScryfallCardsByPath.Values.FirstOrDefault();
                                if (sc2 != null)
                                    _frontArtLibrary.ApplyMetadata(entry.Id, sc2);
                                _frontArtLibrary.ApplyMpcFillDefaults(entry.Id, capturedMpcSource);
                            }
                            StatusLabel.Text = entry != null
                                ? $"Saved \"{libName}\" to front art library"
                                : $"\"{libName}\" already in library";
                        };
                        menu.Items.Add(saveItem);
                    }

                    menu.IsOpen = true;
                    ev.Handled = true;
                };

                tab.OptionsPanel.Children.Add(border);
                tab.AllTiles.Add(new TileInfo(border, label, mpcSource, detail));
                mpcTiles.Add((img, mc, label, detail, mpcSource));
            }

            // Populate source filter now that all tiles exist
            PopulateSourceFilter(tab);
            ApplyFilters(tab);

            // "Browse File" action tile only shown when no actions bar (back mode)
            if (_frontArtLibrary == null)
                AddActionTile(tab, "Browse File...", OnBrowseFile);

            // Phase 2: Stream thumbnail downloads — images fill in as they arrive
            int completed = 0;
            var semaphore = new System.Threading.SemaphoreSlim(8);

            var scryfallDownloadTasks = scryfallTiles.Select(async tile =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var cached = await _scryfall.DownloadAndCacheImageAsync(tile.card, size: "small");
                    if (cached != null)
                    {
                        await Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(cached, UriKind.Absolute);
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.DecodePixelWidth = 150;
                                bmp.EndInit();
                                bmp.Freeze();
                                tile.img.Source = bmp;
                            }
                            catch { /* image load failed — placeholder stays */ }

                            // Update the mutable path captured by click handlers
                            // We need to find the tile's index and update its closure
                        });

                        // Track card by path for OK-click upgrade
                        if (!shown.Contains(cached))
                        {
                            shown.Add(cached);
                            tab.ScryfallCardsByPath[cached] = tile.card;
                        }
                    }
                    var done = System.Threading.Interlocked.Increment(ref completed);
                    _ = Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloaded {done}/{totalImages}...");
                }
                finally { semaphore.Release(); }
            }).ToList();

            var mpcDownloadTasks = mpcTiles.Select(async tile =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var cached = await _mpcFill.DownloadAndCacheImageAsync(tile.card, thumbnail: true);
                    if (cached != null)
                    {
                        await Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                var bmp = new BitmapImage();
                                bmp.BeginInit();
                                bmp.UriSource = new Uri(cached, UriKind.Absolute);
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.DecodePixelWidth = 150;
                                bmp.EndInit();
                                bmp.Freeze();
                                tile.img.Source = bmp;
                            }
                            catch { /* image load failed — placeholder stays */ }
                        });

                        if (!shown.Contains(cached))
                        {
                            shown.Add(cached);
                            tab.MpcFillCardsByPath[cached] = tile.card;
                        }
                    }
                    var done = System.Threading.Interlocked.Increment(ref completed);
                    _ = Dispatcher.BeginInvoke(() => StatusLabel.Text = $"Downloaded {done}/{totalImages}...");
                }
                finally { semaphore.Release(); }
            }).ToList();

            await Task.WhenAll(scryfallDownloadTasks.Concat(mpcDownloadTasks));
            StatusLabel.Text = $"{shown.Count} option(s) found";
            SpinnerDot.Visibility = Visibility.Collapsed;
```

**IMPORTANT:** The above code has a closure problem — `tilePath` is captured by the click handlers but never updated. We need a wrapper object to make the path mutable. Replace the `string? tilePath = null;` pattern with a single-element array: `var tilePathRef = new string?[1];` and use `tilePathRef[0]` everywhere instead of `tilePath`. Here is the corrected approach — use this class added at the top of the ArtSelectorDialog class (inside the class, near the other records/nested classes):

```csharp
        private class MutablePath { public string? Value; }
```

Then in the tile creation loops, use `var pathRef = new MutablePath();` and reference `pathRef.Value` in closures. After download completes, set `pathRef.Value = cached;` inside `Dispatcher.BeginInvoke`.

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs
git commit -m "feat: restructure LoadFrontOptions for progressive tile loading"
```

---

### Task 3: Fix the mutable path closure pattern

**Files:**
- Modify: `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs`

Task 2's click handlers reference `tilePath` which is captured by closure. Because download happens later, we need a mutable reference wrapper so the click handler sees the updated path after download completes.

- [ ] **Step 1: Add MutablePath helper class**

Inside the `ArtSelectorDialog` class, after the `TileInfo` record (after line 45), add:

```csharp
        private class MutablePath { public string? Value; }
```

- [ ] **Step 2: Update Scryfall tile creation to use MutablePath**

In the Scryfall tile creation loop (inside `LoadFrontOptions`), replace:
```csharp
                string? tilePath = null;
```
with:
```csharp
                var pathRef = new MutablePath();
```

And replace all references to `tilePath` with `pathRef.Value` in the click handlers and context menu handlers.

- [ ] **Step 3: Update MPC tile creation to use MutablePath**

Same pattern — replace `string? tilePath = null;` with `var pathRef = new MutablePath();` and all `tilePath` references with `pathRef.Value`.

- [ ] **Step 4: Update download completion to set path**

In the Scryfall download task, after setting the image source inside `Dispatcher.BeginInvoke`, add:
```csharp
                            pathRef.Value = cached;
```

In the MPC download task, same pattern:
```csharp
                            pathRef.Value = cached;
```

(Where `pathRef` is the corresponding tile's `MutablePath` instance — this requires capturing `pathRef` in the download lambda, which happens naturally since the download tasks are created in the same loop that creates `pathRef`.)

**IMPORTANT:** The download lambdas in Task 2 use `tile` from the `Select` — the `pathRef` must be passed alongside via the tile tuple or captured separately. The cleanest approach: change the Scryfall tiles list type to include the pathRef:

Change:
```csharp
var scryfallTiles = new List<(Image img, ScryfallCard card, string label, string detail)>();
```
to:
```csharp
var scryfallTiles = new List<(Image img, ScryfallCard card, string label, string detail, MutablePath pathRef)>();
```

And `scryfallTiles.Add((img, sc, label, detail, pathRef));`

Same for MPC tiles:
```csharp
var mpcTiles = new List<(Image img, MpcFillCard card, string label, string detail, string mpcSource, MutablePath pathRef)>();
```

Then in the download lambdas, access `tile.pathRef.Value = cached;` inside the Dispatcher.

- [ ] **Step 5: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs
git commit -m "fix: use MutablePath wrapper for progressive tile click handler closures"
```

---

### Task 4: Remove the 200+ result confirmation dialog

**Files:**
- Modify: `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs`

- [ ] **Step 1: Verify and remove**

If Task 2 already removed the `if (totalImages > 200)` block as part of the full replacement, verify it's gone. If it's still present, remove the entire block:

```csharp
            // Warn the user if there are a lot of results to cache
            if (totalImages > 200)
            {
                var answer = MessageBox.Show(
                    ...
                    return;
                }
            }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit (if changes were needed)**

```bash
git add MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs
git commit -m "feat: remove 200+ result confirmation dialog"
```

---

### Task 5: Update LoadBackOptionsAsync for progressive Scryfall back face

**Files:**
- Modify: `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs`

The Scryfall back face download currently blocks with `await` before adding the tile. Change it to add a placeholder tile first, then fill in the image.

- [ ] **Step 1: Replace the Scryfall back face section**

In `LoadBackOptionsAsync`, find the section starting with `// Search Scryfall for back face`. Replace the block (the `if (_scryfall != null ...)` block from lines 477-500) with:

```csharp
            // Search Scryfall for back face (MDFCs, transform cards, etc.)
            if (_scryfall != null && !string.IsNullOrEmpty(_card.Name)
                && (_card.IsDoubleFaced || !string.IsNullOrEmpty(_card.OriginalBackArtworkPath)))
            {
                StatusLabel.Text = "Searching Scryfall for back face...";
                try
                {
                    var sc = await _scryfall.GetCardByNameAsync(_card.Name);
                    if (sc?.GetBackImageUrl() != null)
                    {
                        string label = sc.CardFaces?.Count > 1
                            ? $"{sc.CardFaces[1].Name} (Scryfall)"
                            : "Back Face (Scryfall)";
                        string detail = $"Scryfall | {sc.SetName} #{sc.CollectorNumber}";
                        var (border, img) = Controls.ArtTileBuilder.CreatePlaceholderTile(label, detail);

                        var pathRef = new MutablePath();
                        string capturedLabel = label;
                        string capturedDetail = detail;
                        border.MouseLeftButtonUp += (_, _) =>
                        {
                            if (pathRef.Value != null) SelectOption(tab, capturedLabel, pathRef.Value, capturedDetail, border);
                        };
                        border.MouseLeftButtonDown += (_, ev) =>
                        {
                            if (ev.ClickCount == 2 && pathRef.Value != null)
                            {
                                SelectOption(tab, capturedLabel, pathRef.Value, capturedDetail, border);
                                OkClick(null!, null!);
                            }
                        };

                        tab.OptionsPanel.Children.Add(border);
                        tab.AllTiles.Add(new TileInfo(border, label, "Scryfall", detail));

                        // Download thumbnail asynchronously — don't block
                        var capturedSc = sc;
                        _ = Task.Run(async () =>
                        {
                            var cachedBack = await _scryfall.DownloadAndCacheImageAsync(capturedSc, back: true, size: "small");
                            if (cachedBack != null)
                            {
                                await Dispatcher.BeginInvoke(() =>
                                {
                                    try
                                    {
                                        var bmp = new BitmapImage();
                                        bmp.BeginInit();
                                        bmp.UriSource = new Uri(cachedBack, UriKind.Absolute);
                                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                                        bmp.DecodePixelWidth = 150;
                                        bmp.EndInit();
                                        bmp.Freeze();
                                        img.Source = bmp;
                                        pathRef.Value = cachedBack;
                                    }
                                    catch { }
                                });

                                if (!shown.Contains(cachedBack))
                                {
                                    shown.Add(cachedBack);
                                    tab.ScryfallCardsByPath[cachedBack] = capturedSc;
                                }
                            }
                        });
                    }
                }
                catch (Exception ex) { Log.Warning(ex, "Scryfall back face lookup failed for {CardName}", _card.Name); }
            }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs
git commit -m "feat: progressive Scryfall back face loading in LoadBackOptionsAsync"
```

---

### Task 6: Update LoadTabContentAsync status flow

**Files:**
- Modify: `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs`

The `LoadTabContentAsync` method currently sets the final status and hides the spinner after `LoadFrontOptions`/`LoadBackOptionsAsync` returns. Since Phase 2 downloads now happen inside those methods with their own status updates, the parent method's final status update may overwrite the streaming progress. Adjust so the spinner is hidden and the final count is set at the end of the download streaming, not in `LoadTabContentAsync`.

- [ ] **Step 1: Remove redundant status/spinner update from LoadTabContentAsync**

In `LoadTabContentAsync`, find these lines (currently after the if/else for front/back loading):

```csharp
            StatusLabel.Text = $"{shown.Count} option(s) found";
            SpinnerDot.Visibility = Visibility.Collapsed;
            PopulateSourceFilter(tab);
            ApplyFilters(tab);
```

Since `LoadFrontOptions` now handles `PopulateSourceFilter`, `ApplyFilters`, status text, and spinner hiding at the end of Phase 2, remove the duplicate calls for the front tab case. Keep them for the back tab since library loading still needs them.

Replace the block with:

```csharp
            if (tab.Mode == ArtSelectorMode.Back)
            {
                StatusLabel.Text = $"{shown.Count} option(s) found";
                SpinnerDot.Visibility = Visibility.Collapsed;
                PopulateSourceFilter(tab);
                ApplyFilters(tab);
            }
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs
git commit -m "fix: adjust LoadTabContentAsync status flow for progressive front loading"
```

---

### Task 7: Final build and smoke test

- [ ] **Step 1: Full rebuild**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run tests**

Run: `dotnet test`
Expected: All non-UI tests pass (5 pre-existing UI smoke test failures are expected).

- [ ] **Step 3: Commit plan doc**

```bash
git add docs/
git commit -m "docs: add progressive art loading implementation plan"
```
