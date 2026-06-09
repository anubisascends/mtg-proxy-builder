# UI Restructure Design — Menu Bar, Icon Toolbar, Sidebar Accordion

**Date:** 2026-06-09
**Status:** Approved

## Overview

Replace the AvalonDock-based dockable panel layout with a standard Windows app structure: menu bar, icon toolbar, project tab bar, and a fixed sidebar with collapsible accordion sections. Removes the AvalonDock dependency entirely.

## Decisions

- **Menu bar:** File, Edit, Cards, Tools, Help
- **Icon toolbar:** Segoe MDL2 Assets icons, no text labels, tooltips only
- **Tab bar:** Stays as-is (horizontal tabs between toolbar and content)
- **Sidebar:** Fixed 300px right panel with 5 accordion sections (Search, Import, Card Details, Layout, Storage)
- **Accordion behavior:** Multiple sections can be open simultaneously, state persists to settings
- **AvalonDock:** Removed entirely (packages, XAML, dock layout persistence)

## Window Layout

```
+----------------------------------------------------------+
| File  Edit  Cards  Tools  Help           (Menu bar)      |
+----------------------------------------------------------+
| [New][Open][Save] | [Undo][Redo] | [+File][PDF] | [Art][Settings]  (Toolbar) |
+----------------------------------------------------------+
| [Project 1 x] [Project 2 x] [+ New] [Open]   (Tab bar) |
+----------------------------------------------------------+
|                              |                           |
|                              |  [v Search      ]         |
|    Card Grid / Canvas        |  [v Import      ]         |
|                              |  [v Card Details ]         |
|                              |  [v Layout       ]         |
|                              |  [v Storage      ]         |
|                              |                           |
+----------------------------------------------------------+
| Ready  |  12 cards  |  v0.5.0            (Status bar)    |
+----------------------------------------------------------+
```

- Menu bar, toolbar, and status bar are always visible
- Tab bar, sidebar, and canvas only visible when a project is open
- Welcome screen shown when no project is open (replaces content area)

## Menu Bar

| Menu | Item | Shortcut |
|------|------|----------|
| **File** | New Project | Ctrl+N |
| | Open Project... | Ctrl+O |
| | Save | Ctrl+S |
| | Save As... | Ctrl+Shift+S |
| | --- | |
| | Export PDF... | Ctrl+E |
| | Export SVG Cut Lines... | |
| | --- | |
| | Exit | |
| **Edit** | Undo | Ctrl+Z |
| | Redo | Ctrl+Y |
| | --- | |
| | Clear All Cards | |
| **Cards** | Add from File... | |
| | Import Deck... | |
| | Import MPCFill XML... | |
| | --- | |
| | Remove Selected | Del |
| **Tools** | Art Library | |
| | Settings | |
| **Help** | Check for Updates | |
| | About | |

All menu items bind to existing commands — no new logic needed.

## Icon Toolbar

Single row of 32x32 icon buttons using Segoe MDL2 Assets font. Grouped with vertical separators.

| Group | Action | Glyph Code | Tooltip |
|-------|--------|-----------|---------|
| Project | New | `\uE7C3` | New Project (Ctrl+N) |
| | Open | `\uE8E5` | Open Project (Ctrl+O) |
| | Save | `\uE74E` | Save (Ctrl+S) |
| Edit | Undo | `\uE7A7` | Undo (Ctrl+Z) |
| | Redo | `\uE7A6` | Redo (Ctrl+Y) |
| Cards | Add File | `\uE710` | Add Card from File |
| | Export PDF | `\uE8A5` | Export PDF (Ctrl+E) |
| Global | Art Library | `\uE8B9` | Art Library |
| | Settings | `\uE713` | Settings |

**Button style:** Transparent background, #CCC icon, #3E3E42 hover background, no text labels (tooltips only).

## Sidebar Accordion

### Structure

Fixed 300px right panel. Each section has a clickable header bar (chevron + title) and collapsible content area.

**Section header style:**
- Background: #2D2D30
- Hover: #3E3E42
- Chevron: `\u25B8` (▸) collapsed, `\u25BE` (▾) expanded
- Title: #CCC, 12px
- Padding: 8px

### Sections

**Search**
- Scryfall/MPCFill radio toggle
- Search textbox + Enter to search
- Advanced search expander (Scryfall fields)
- Results list with thumbnails
- Double-click or button to add card
- MPCFill: name filter, DPI filter, fuzzy toggle, favorites toggle

**Import**
- Deck URL textbox + Import button
- "Skip duplicates" checkbox
- Import MPCFill XML button

**Card Details**
- Selected card name, metadata display
- Scryfall lookup textbox + button
- "Select Art..." / "Select Card Back..." buttons
- Overlay text textbox
- Quantity control

**Layout**
- Page size preset dropdown + landscape toggle
- Print mode dropdown
- Card size preset dropdown + custom dimensions
- Bleed width
- Grid override (columns/rows)
- Card outline settings (enable, color, alignment, radius, type, line type, weight)
- Silhouette Cameo settings (registration marks, SVG export, mark dimensions)

**Storage**
- Cache size display
- Clear Cache button

### Behavior

- Multiple sections can be open simultaneously
- Expanded/collapsed state persisted to `app_settings.json` (per section name → bool)
- Sidebar scrollable if total expanded content exceeds height
- Sidebar hidden on welcome screen (no project open)

## Removed

- `Dirkster.AvalonDock` NuGet package
- `Dirkster.AvalonDock.Themes.VS2013` NuGet package
- All AvalonDock XAML markup (LayoutAnchorablePane, LayoutDocument, DockManager, etc.)
- `dock_layout.xml` persistence file
- `LoadDockLayout` / `SaveDockLayout` / `CapturePanelContents` methods from MainWindow.xaml.cs

## Files

| File | Action | Description |
|------|--------|-------------|
| `MTGProxyBuilder.UI/MainWindow.xaml` | Major rewrite | Menu bar + toolbar + tab bar + grid/sidebar layout |
| `MTGProxyBuilder.UI/MainWindow.xaml.cs` | Major rewrite | Remove dock code, add menu/toolbar bindings |
| `MTGProxyBuilder.UI/Controls/SidebarSection.xaml` + `.cs` | Create | Reusable accordion section control |
| `MTGProxyBuilder.UI/MTGProxyBuilder.UI.csproj` | Modify | Remove AvalonDock packages |
| `MTGProxyBuilder.UI/ViewModels/ShellViewModel.cs` | Modify | Add sidebar state persistence, SaveAs command |
| `MTGProxyBuilder.Core/Services/AppSettingsService.cs` | Modify | Add sidebar expanded states to settings |
| `MTGProxyBuilder.Tests/Integration/UiSmokeTests.cs` | Modify | Update for menu/toolbar/sidebar |

### Unchanged

- All dialog windows
- Core services, models, business logic
- Canvas/grid editor
- ViewModel command logic (same commands, new bindings)
