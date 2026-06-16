# Getting Started

## Download

1. Go to the [Releases](../../releases) page
2. Download the latest `TCGProxyBuilder-vX.X.X-win-x64.zip`
3. Extract the ZIP to any folder
4. Run `tcg-proxy-builder.exe` — no installation or .NET runtime required

The release is a self-contained single-file executable for Windows 10/11 (64-bit).

## Building from Source

### Prerequisites
- .NET SDK 10.0 or later
- Windows 10/11
- curl (ships with Windows 10+, used for Moxfield API)

### Build
```bash
cd mtg-proxy-builder
dotnet build
```

### Run
```bash
cd MTGProxyBuilder.UI
dotnet run
```

### Test
```bash
# Unit + integration tests (fast, no UI)
dotnet test --filter "Category!=UI"

# UI smoke tests (launches the app via FlaUI)
dotnet test --filter "Category=UI"

# All tests
dotnet test
```

## Quick Start

1. Launch the application — you'll see a welcome screen
2. Click **"New Project"** or **"Open Project"** (or press Ctrl+N / Ctrl+O)
3. Add cards using one of the import methods (see [Card Search & Import](card-search-import.md))
4. Configure layout settings in the Layout panel (see [Print & PDF Export](print-pdf-export.md))
5. Export to PDF
6. Open additional projects in new tabs — each is independent

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+N | New project |
| Ctrl+O | Open project |
| Ctrl+W | Close active project tab |
| Ctrl+S | Save active project |
| Ctrl+E | Export PDF |
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+V | Paste image or Piltover Archive URL from clipboard |
| Ctrl+A | Select all cards |
| Ctrl+D | Deselect all |
| Ctrl+Shift+I | Invert selection |
| Delete | Delete selected cards |
| Ctrl+Mouse Wheel | Zoom in/out |
| Shift+Mouse Wheel | Horizontal scroll |
| Middle-click + drag | Pan canvas |
| Escape | Deselect all cards |
| Enter (search box) | Execute search |
| Enter (import URL) | Import deck |
| Double-click (search result) | Add card to project |
| Double-click (canvas card) | Open art selector |
| Right-click (canvas) | Context menu |
| Ctrl+Click (canvas) | Toggle card in selection |
| Shift+Click (canvas) | Range select from last click |
| Arrow keys | Navigate between cards |
| Ctrl+Arrow | Jump to next/previous page |
| Shift+Arrow | Range selection via keyboard |

## File Locations

| Location | Path | Contents |
|----------|------|----------|
| App Settings | `%AppData%/MTGProxyBuilder/app_settings.json` | Default settings, MPCFill filters |
| Logs | `%AppData%/MTGProxyBuilder/Logs/` | Rolling daily log files (7-day retention) |
| Image Cache | `%AppData%/MTGProxyBuilder/ImageCache/` | Downloaded card images + metadata |
| Bleed Cache | `%AppData%/MTGProxyBuilder/BleedCache/` | Bleed-processed images |
| Front Art Library | `%AppData%/MTGProxyBuilder/FrontArtLibrary/` | Saved front art + catalog.json |
| Back Art Library | `%AppData%/MTGProxyBuilder/BackArtLibrary/` | Saved back art + catalog.json |
| MPCFill Favorites | `%AppData%/MTGProxyBuilder/mpcfill_favorite_sources.json` | Favorited sources |
| Projects | User-chosen location | `.mtgproj` ZIP archives |
