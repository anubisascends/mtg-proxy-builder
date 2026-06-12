# Project Management & Settings

## Projects

- **Multi-project tabs** — open multiple projects simultaneously, each with independent state, undo stack, and file
- **Portable project files** — `.mtgproj` files are self-contained ZIP archives bundling all artwork
- **Undo / Redo** — 50-level undo stack per project (Ctrl+Z / Ctrl+Y); covers all card operations
- **Unsaved changes prompt** — each project prompts individually on close
- **Save:** Ctrl+S creates a `.mtgproj` ZIP file containing all artwork
- **Open:** extracts and loads the project with all images intact

## Sort & Filter

- Filter by name, type, rarity, color
- Sort by CMC, name, set, artist, and more
- Permanently apply sort order

## Card Metadata

Full metadata stored per card: mana cost, type line, oracle text, rarity, colors, set, artist, power/toughness, loyalty, keywords from Scryfall. Riftbound cards store energy, might, power, type, description, and tags from Piltover Archive.

## Application Settings

Accessible from the toolbar, persists to `app_settings.json`:

- **Default token text** — customizable text for token card overlays
- **Default page size** — A4, A3, Letter, Legal, Tabloid
- **Default bleed** — default bleed width for new projects
- **Default card size preset** — applied to new projects automatically
- **Update check toggle** — enable/disable automatic version checking
- **Art library paths** — browse for an existing `catalog.json` to point libraries at a custom directory
- **Sidebar font scaling** — adjustable 9-18pt slider

### MPCFill Settings

- Default sort order, min/max DPI, max file size
- Fuzzy search toggle
- Cardback filtering
- Card types: Cards, Tokens, Card Backs
- Languages: EN, JA, FR, DE, ES, IT, PT, ZH, RU, AR, SA
- Content filters: exclude NSFW, exclude AI-generated art
- Favorites-only mode

## UX Features

- **Dark theme** — VS2013 dark theme throughout
- **Sidebar accordion** — collapsible sections: Search, Import, Card Details, Layout, Storage
- **Resizable sidebar** — drag the splitter (200-600px)
- **Busy spinner** — animated overlay with step-by-step progress during all network operations
- **Async image loading** — card images load on a background thread with memory caching
- **Mana symbol rendering** — 109 embedded SVG mana symbols from [CardConjurer](https://github.com/MrTeferi/cardconjurer)
- **Set symbol display** — 2,413 embedded SVG set symbols from [mtg-vectors](https://github.com/Investigamer/mtg-vectors)
- **Automatic update check** — checks GitHub releases on startup; banner with download link when a new version is available
- **Structured logging** — Serilog with rolling daily log files (7-day retention); global exception handlers with user-friendly error dialog
- **Auto-cleanup** — bleed cache and extracted project images cleared on startup; image cache cleared on exit

## Troubleshooting

### Application won't start
- Ensure Windows 10/11 64-bit
- If building from source, ensure .NET 10.0+ SDK: `dotnet --version`
- Check `%AppData%/MTGProxyBuilder/Logs/` for crash details

### Scryfall search returns errors
- Check internet connection
- Scryfall rate-limits to ~10 req/s (respected automatically)

### MPCFill search returns no results
- Ensure you're online (271+ sources are fetched on first search)
- Uncheck "Favs only" if no favorites are set
- Try unchecking "Fuzzy" for exact name matches
- Check Settings for active filters that may be excluding results
- In the Source Manager, click "Refresh" if the source list is empty

### Moxfield import fails with 403
- Moxfield uses Cloudflare protection; the app uses curl to bypass this
- Ensure curl is available (ships with Windows 10+)
- Private decks cannot be imported

### Piltover Archive import fails
- The site uses Cloudflare; the app sets a browser User-Agent to bypass basic protection
- If the page format changes, the parser may need updating
- Check the Logs folder for detailed error information

### PDF generation is slow
- Bleed processing converts images to JPEG (cached after first time)
- Large projects (100+ unique images) may take a few seconds

### Cache using too much disk space
- Go to Layout tab, STORAGE section, click "Clear Cache"
- Bleed cache and extracted projects are auto-cleaned on startup
