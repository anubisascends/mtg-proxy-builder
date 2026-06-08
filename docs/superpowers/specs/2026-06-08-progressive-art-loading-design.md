# Progressive Art Selector Loading Design

**Date:** 2026-06-08
**Status:** Approved

## Overview

Restructure the ArtSelectorDialog to load artwork progressively instead of blocking until all downloads complete. Tiles appear immediately as placeholders with card names, and images stream in as thumbnails download. The dialog is fully interactive throughout.

## Decisions

- **Always progressive** — No threshold; all result sets stream regardless of size
- **Placeholder tiles** — Tiles appear immediately with card name, grey background, no image
- **Stream all thumbnails** — No viewport-aware lazy loading; download all thumbnails concurrently and fill in as they arrive
- **Remove 200+ dialog** — The confirmation warning for large result sets is removed since progressive loading handles them gracefully

## Tile Lifecycle

Each tile progresses through states:

1. **Placeholder** — Tile border + card name label + grey background. Fully interactive (clickable, shows tooltip with card info). No image.
2. **Loaded** — Image swapped in via `Dispatcher.BeginInvoke` when thumbnail download completes. Tile is identical to current appearance.
3. **Failed** — If download fails, tile remains as placeholder with a subtle "No image" indicator. Still selectable — the full-res upgrade on OK click may succeed independently.

## Download Streaming Flow

### Current flow
```
Search APIs (concurrent) → Download ALL images (blocked) → Create tiles → Add to panel
```

### New flow
```
Search APIs (concurrent) → Create all placeholder tiles → Add to panel → Stream downloads → Swap in images as each arrives
```

### Detail

1. Scryfall and MPCFill API searches run concurrently via `Task.WhenAll` (unchanged)
2. On completion, all placeholder tiles are created and added to the WrapPanel synchronously — user immediately sees full result count
3. Thumbnail downloads fire with `SemaphoreSlim(8)` concurrency
4. Each download completion calls `Dispatcher.BeginInvoke` to:
   - Set the tile's `Image.Source` to the downloaded bitmap
   - Update `ScryfallCardsByPath` / `MpcFillCardsByPath` with the real cached path
   - Update status label: `"Downloaded 12/47..."`
5. Dialog is fully interactive during downloads — user can scroll, filter, search, select tiles, switch tabs
6. If user clicks OK on a placeholder tile (no image yet), the full-res download in `OkClick` proceeds normally

### Status Label Progression

`"Searching..."` → `"Found 47 results, downloading..."` → `"Downloaded 12/47..."` → `"47 option(s) found"`

## Code Changes

### Files Modified

| File | Changes |
|------|---------|
| `MTGProxyBuilder.UI/Dialogs/ArtSelectorDialog.xaml.cs` | Restructure `LoadFrontOptions` and `LoadBackOptionsAsync` into two-phase (placeholder + streaming) |

### LoadFrontOptions Restructuring

**Phase 1 — Synchronous after search:**
- Create all Scryfall placeholder tiles (from search results)
- Create all MPCFill placeholder tiles (from search results)
- Add all tiles to WrapPanel immediately
- Populate tracking dictionaries with temporary placeholder keys
- Populate source filter and apply filters

**Phase 2 — Async streaming:**
- Fire off all thumbnail downloads concurrently (8 semaphore)
- On each completion: swap image into tile, update tracking dictionaries with real path
- Update status label progressively
- On all complete: final status update

### LoadBackOptionsAsync Restructuring

Same two-phase pattern for the Scryfall back face download. Library entries already use deferred image loading and remain unchanged.

### Removed

- The 200+ result confirmation dialog (`if (totalImages > 200)` block)

### Unchanged

- Library entry loading (already uses deferred thumbnails)
- `OkClick` full-res upgrade logic
- Filter/search/source filter behavior
- Tab switching mechanics
- Selection/OK/Cancel mechanics
- "Save to Library" context menu behavior
