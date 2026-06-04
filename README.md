# DayNote

A cross-platform desktop application, with macOS as the primary target, for managing many
plain-text notes organized into daily-journal-style collections. DayNote is persistence-first:
everything written is intended to be kept, so the live data files are the source of truth.

DayNote is the successor to *quickdeck*, porting its proven mechanisms and generalizing
quickdeck's flat panes into a two-level model of **notebooks** containing **notes**.

## Domain model

- **Notebook** — one `.daynote` file (TOML); an ordered collection of notes plus metadata.
- **Note** — a single text entry: identity, title, timestamps, attachment references, body.
- **Attachment** — a file associated with a note, stored beside the notebook.

## Persistence stores

1. Notebook data files (`.daynote`, TOML) — human-owned and portable.
2. Application configuration and UI state (JSON under `~/.daynote/`).
3. A backup store (`~/.daynote/backups.sqlite`).
4. Logs (`~/.daynote/logs/`).

## Architecture

- **DayNote.Core** — framework-independent: domain model, TOML reader/writer, body cleanup,
  character counting, identifier generation, the backup store, and config/state. No UI deps.
- **DayNote.Desktop** — the Avalonia application and its view models (MVVM via
  CommunityToolkit), depending on Core.

Side effects (file and database I/O) live at the edges; dependencies point inward.

## Build & run

```sh
dotnet build
dotnet run --project src/DayNote.Desktop
```

### Distribution

- **macOS** (primary): `scripts/run.command` publishes a self-contained build, assembles an
  unsigned `DayNote.app` under `publish/`, ad-hoc signs it, and launches it. No Apple Developer
  identity is used.
- **Windows**: `scripts/run.ps1` restores and launches the app from source for quick local
  runs. To produce a self-contained build, use
  `dotnet publish src/DayNote.Desktop -c Release -r win-x64 --self-contained`.

## License

MIT © 2026 Yoshinao Inoguchi
