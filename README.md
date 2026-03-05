<h1 align="center">Upcoming Media — Jellyfin Plugin</h1>

<p align="center">
  Display <strong>upcoming movies &amp; TV shows</strong> directly on your Jellyfin home page.<br/>
  Admins can search TMDb, set release dates, manage dummy files, and curate a "Coming Soon" experience for all users.
</p>

---

## Features

| Feature | Description |
|---------|-------------|
| **Home Page Widget** | Shows an "Upcoming" section on every user's home page |
| **TMDb Integration** | Search and auto-fill metadata (poster, backdrop, overview, genres, trailer) |
| **Status Lifecycle** | Automatic transitions: *Coming Soon* → *Available* → *Expired* |
| **Library Folder System** | Auto-creates library folders; rename `.real` → ready file to activate |
| **Dummy File Support** | Optional placeholder MKV so Jellyfin indexes the folder early |
| **Scheduled Task** | Background task checks dates and auto-swaps files when available |
| **Notification Subscriptions** | Users can subscribe to get notified when items become available |

## Screenshots

> _Add screenshots of the home widget and config page here._

## Installation

### Manual Install (Recommended for now)

1. Download `JellyfinUpcomingMedia.dll` from the [latest release](../../releases/latest).
2. Place it in your Jellyfin plugin directory:
   - **Windows:** `%LocalAppData%\Jellyfin\plugins\Upcoming Media_1.0.0.0\`
   - **Linux:** `~/.local/share/jellyfin/plugins/Upcoming Media_1.0.0.0/`
   - **Docker:** `/config/plugins/Upcoming Media_1.0.0.0/`
3. Restart Jellyfin.
4. Go to **Dashboard → Plugins** — you should see "Upcoming Media".

### Build from Source

```bash
git clone https://github.com/YOUR_USERNAME/jellyfin-plugin-upcoming-media.git
cd jellyfin-plugin-upcoming-media
dotnet build -c Release
```

The built DLL will be at `bin/Release/net9.0/JellyfinUpcomingMedia.dll`.

## Configuration

1. Navigate to **Dashboard → Plugins → Upcoming Media → Settings**.
2. Enter your **TMDb API Key** (get one free at [themoviedb.org](https://www.themoviedb.org/settings/api)).
3. Add items manually or search TMDb.
4. Set **Available Dates** — the plugin will auto-transition items on that date.

### File Swap System

The plugin can manage library folders for upcoming content:

- When you add an item, a folder is auto-created in your library path (e.g., `F:\Movies\Movie Name (2026)\`).
- Drop your actual media file with a `.real` extension (e.g., `movie.mkv.real`) into that folder.
- When the available date arrives (or you click "Activate Now"), the `.real` extension is removed and Jellyfin picks it up on the next library scan.
- Optionally create a dummy `.mkv` placeholder so the folder appears in Jellyfin early.

## Requirements

- **Jellyfin Server** 10.11.x+
- **.NET 9.0** runtime (included with Jellyfin 10.11+)
- **TMDb API Key** (free) for metadata search

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/UpcomingMedia/Items` | Get all upcoming items |
| POST | `/UpcomingMedia/Items` | Create a new item |
| POST | `/UpcomingMedia/Items/AddFromTmdb` | Add item from TMDb search |
| POST | `/UpcomingMedia/Items/{id}/CreateDummy` | Create dummy MKV file |
| DELETE | `/UpcomingMedia/Items/{id}/DeleteDummy` | Remove dummy file |
| POST | `/UpcomingMedia/Items/{id}/SwapFile` | Swap .real → actual file |
| POST | `/UpcomingMedia/Items/{id}/ScanRealFile` | Check if .real file exists |
| GET | `/UpcomingMedia/FetchTrailer` | Fetch YouTube trailer from TMDb |
| POST | `/UpcomingMedia/Notifications/Subscribe` | Subscribe to notifications |

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the [MIT License](LICENSE).

---

<p align="center">
  Made with ❤️ for the Jellyfin community
</p>
