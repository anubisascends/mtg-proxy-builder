# Art Selection & Libraries

## Art Selector Dialog

Click any card (or double-click on the canvas) to browse artwork from Scryfall (all printings) and MPCFill (community art) in a thumbnail grid with a zoomable preview panel.

**Features:**
- **Progressive loading** — placeholder tiles appear instantly, then thumbnails stream in (8 concurrent downloads)
- **Small thumbnail optimization** — browsing uses small images; full resolution is fetched only when you commit a selection
- **Tile info panel** — each tile shows clickable source name (cyan) and DPI; clicking the source adds a filter pill
- **Tags** — MPCFill tiles with tags show a dropdown button; clicking a tag adds a filter pill
- **Pill filter bar** — type `dpi:>800`, `source:Chilli_Axe`, `tag:Retro`, or free text; supports `=`, `!`, `>`, `<`, `>=`, `<=`, `in[...]`, AND/OR logic, and parentheses
- **Local-first search** — the library is checked before querying online APIs
- **Bulk apply** — checkbox to apply front art to all cards with the same name, or back art to all cards without one

## Multi-Select Art Changes

Select multiple cards on the canvas (Ctrl+Click or Shift+Click), then:
- Right-click "Select Front Art" or "Select Card Back" to apply artwork to all selected cards at once
- Also works from the Card Details sidebar buttons

## Front Art Library

A persistent library of saved card art, accessible from the global toolbar without an open project.

- Art is auto-saved when adding cards from MPCFill search results
- **Import Downloaded Art** — bulk-import all cached MPCFill art; thumbnails auto-generated, cache files removed
- **Add from File** — import images manually
- Search by name, filter by source
- Multi-select with Ctrl+Click or Shift+Click; batch delete supported
- When selecting art for a card, library matches load instantly (no network calls)

## Back Art Library

A persistent library of card backs, accessible from the global toolbar.

- **Download MPCFill Card Backs** — populate the library (~460 card backs)
- Preview panel shows DPI, dimensions, and source
- **Set as Default** — auto-applied to new cards
- Search by name, filter by contributor
- Multi-select and batch delete supported
- New cards automatically get the project's most common back art (or the library default)

## Library Management (Both Libraries)

- **Move Library** — relocate all images to a different folder; existing libraries at the destination are merged
- **Export as ZIP** — compressed backup of the entire library
- **Import from ZIP** — restore entries from a previously exported archive (deduplicates by name)
- **Regenerate Thumbnails** — rebuild all cached 200px JPEG thumbnails
- In Settings, browse for an existing `catalog.json` to point the library at a different location without moving files

## MPCFill Source Manager

Browse 271+ community art sources, favorite by clicking the star, filter searches to favorites only. Refresh button to reload from the API. Favorites persist across sessions.
