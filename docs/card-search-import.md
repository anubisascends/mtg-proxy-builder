# Card Search & Import

## Scryfall Search

- Search by name, type, color, mana cost, set, rarity, artist, format legality, and more using Scryfall's full query syntax
- **Advanced Search** — visual query builder with dropdowns for all filter fields (colors, CMC, rarity, power/toughness, format, set, artist, keywords, card properties)
- Click a result to see a preview, or double-click to add it to your project

## MPCFill Search (Community Proxy Art)

- Search community-contributed high-DPI proxy art from MPCFill.com
- Results show thumbnails, source contributor, and DPI
- Use inline filters: name filter, minimum DPI, fuzzy/exact toggle
- Click "Sources..." to manage and favorite art sources (271+ contributors)
- Check "Favs only" to limit searches to your favorite sources
- **MPCFill Tags** — per-card tags from the API (e.g. "Extended-Art", "Frame", "Retro") are captured and displayed
- **Filter Settings** — full control in the Settings dialog: sort order, DPI range, max file size, fuzzy/exact match, card types (Cards/Tokens/Card Backs), 11 languages, content filters (NSFW, AI Art), and cardback filtering

## Deck Import (Moxfield / Archidekt / Piltover Archive)

Paste a deck URL into the Import section and press Enter or click Import. The app auto-detects the source and downloads all artwork.

**Supported sites:**
- **Moxfield** — `moxfield.com/decks/...`
- **Archidekt** — `archidekt.com/decks/...`
- **Piltover Archive (Riftbound)** — `piltoverarchive.com/decks/view/...`

**Options:**
- **Skip duplicates** — skip cards already in the project when importing (basic lands are merged by quantity)
- **Deck refresh** — imported deck URLs are stored on the project; click "Refresh Deck" to re-import from the original URL

**Clipboard shortcut:** Copy a Piltover Archive URL and press Ctrl+V — the app auto-detects and starts the import.

## Riftbound Import (Piltover Archive)

Riftbound decks from [piltoverarchive.com](https://piltoverarchive.com) are imported through the same deck import field as Moxfield/Archidekt. When a Piltover Archive URL is detected:

1. The deck page is fetched and parsed (card data is embedded in the page's React Server Components payload)
2. High-resolution card images are downloaded from the Piltover Archive CDN
3. Card metadata is populated: name, type, description, energy, might, power, rarity, artist, colors, and tags
4. The card size preset is automatically switched to **Riftbound** (63 x 88 mm)
5. Cards are flagged as Riftbound — front art cannot be changed (no Scryfall/MPCFill sources exist), but back art works normally using the standard back art library

**What works for Riftbound cards:**
- Back art selection (double-click when flipped, context menu "Select Card Back", "Match Back Art")
- Drag and drop reordering
- Duplicate, delete, flip
- All print/PDF features

**What's disabled for Riftbound cards:**
- Front art changes (no alternate sources available)
- Scryfall data lookup
- "Select Front Art" context menu item

## MPCFill XML Import

Import a `cards.xml` project file exported from MPCFill's editor:
1. Click "Import cards.xml..." in the Import section
2. Select the exported XML file
3. Card images are downloaded from the original sources

## Text List Import

Import a plain text list of card names:
1. Click "Import Card List..." in the Import section
2. Paste a list of cards (one per line, with optional quantity prefix like `4x Lightning Bolt`)
3. Cards are looked up on Scryfall and artwork is downloaded

## Local Files

- Click "+ File" in the toolbar to add card images from your computer (multi-select supported)
- Supported formats: PNG, JPG, BMP

## Clipboard Paste

- Press Ctrl+V with an image on the clipboard to add it as a "Pasted Image" card
- Press Ctrl+V with a Piltover Archive URL on the clipboard to start a Riftbound deck import
- Pasted image cards cannot have their art changed — they can only be moved, duplicated, or deleted
