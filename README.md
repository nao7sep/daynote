# DayNote

A cross-platform desktop application, with macOS as the primary target, for managing many plain-text notes organized into daily-journal-style collections. DayNote is persistence-first: everything written is intended to be kept, so the live data files are the source of truth.

DayNote is the successor to *quickdeck*, porting its proven mechanisms and generalizing quickdeck's flat panes into a two-level model of **notebooks** containing **notes**.

## Domain model

- **Notebook** — one `.daynote` file (TOML); an ordered collection of notes plus metadata.
- **Note** — a single text entry: identity, title, timestamps, attachment references, body.
- **Attachment** — a file associated with a note, stored beside the notebook.

## Persistence stores

1. Notebook data files (`.daynote`, TOML) — human-owned and portable.
2. Application configuration and UI state (JSON under `~/.daynote/`).
3. A backup store (`~/.daynote/backups.sqlite`).
4. Logs (`~/.daynote/logs/`) — one JSON Lines file per launch, named with the UTC start stamp and nothing else (`yyyymmdd-hhmmss-utc.log`) and kept indefinitely; logs are never pruned or rotated.

## Architecture

- **DayNote.Core** — framework-independent: domain model, TOML reader/writer, body cleanup, character counting, identifier generation, the backup store, and config/state. No UI deps.
- **DayNote.Desktop** — the Avalonia application and its view models (MVVM via CommunityToolkit), depending on Core.

Side effects (file and database I/O) live at the edges; dependencies point inward.

## Build & run

```sh
dotnet build
dotnet run --project src/DayNote.Desktop
dotnet test          # logger unit tests under tests/DayNote.Tests
```

Verbose `debug`-level logging is for developers: it is on automatically in a `Debug` build, and can be enabled for a `Release` build by setting `DAYNOTE_DEBUG=1`. It is off by default on end-user machines so logs never flood a user's disk.

### Distribution

Each launcher is a `scripts/<name>.command` (macOS) / `scripts/<name>.ps1` (Windows) pair.

- **macOS** (primary):
  - `run-dev` — runs the app from source with `dotnet run`; fast, for active coding. TCC-gated features (Desktop, Documents, Downloads) need the signed bundle, so use `run-built`/`rebuild` to exercise those.
  - `run-built` — launches the existing signed `DayNote.app` under `publish/` without rebuilding.
  - `rebuild` — publishes a self-contained `Release` build, assembles `DayNote.app` under `publish/`, ad-hoc signs it (no Apple Developer identity is used), and launches it. Run after changing source.
- **Windows**:
  - `run-dev` — restores and launches the app from source with `dotnet run`, for quick local runs.
  - `run-built` — launches the existing published executable without rebuilding.
  - `rebuild` — publishes a self-contained `Release` build (`dotnet publish src/DayNote.Desktop -c Release -r win-x64 --self-contained`) and launches it.

## License

MIT © 2026 Yoshinao Inoguchi
