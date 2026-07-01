# DayNote

DayNote is a macOS-first (also Windows) desktop app for plain-text notes organized into **binders**. A binder is a `.daynote` file; its attachments live in a folder beside it, so the two move together. A note is a single dated text entry with optional file attachments.

Everything is persistence-first: writes are atomic, the live files are the source of truth, and nothing you write is silently discarded. DayNote suits anyone who wants a fast, local, file-owned home for a lot of small notes — a daily-journal workflow is the intended direction, though today the model is a flat two-level one (binders contain notes, no date grouping yet). Succeeds *quickdeck*, generalizing its flat panes into binders-of-notes.

## Download

Prebuilt builds for **macOS (Apple Silicon)** and **Windows (x64)** are on the [Releases](https://github.com/nao7sep/daynote/releases) page — a `.dmg` / `setup.exe` installer or a portable `.zip`. The builds are **self-contained** (no .NET install needed) and **unsigned**, so the OS warns the first time you open one:

- **macOS** — right-click the app and choose **Open** (or run `xattr -dr com.apple.quarantine /Applications/DayNote.app`).
- **Windows** — on the SmartScreen prompt, click **More info → Run anyway**.

## Requirements

- **macOS** (Apple Silicon) or **Windows (x64)** to run a prebuilt download — self-contained, nothing to install.
- **.NET 10 SDK** only if you build from source.

## Features

- **Binders of plain-text notes** — many notes per `.daynote` file, each with a title and body.
- **Attachments** — associate files with a note; add by drag-and-drop, reorder in place.
- **Lifecycle status** — draft → ready → published → expired; published and expired notes are locked until moved back to draft or ready.
- **Character counting** — live word/character counts plus an X/Twitter-weighted count against the 280 limit.
- **Autosave** — debounced save as you type; flushes on close and quit.
- **Dark "Twilight" theme**, keyboard-driven throughout.

## Getting started

Run from source — the fastest way to try it:

- **macOS:** `scripts/run-dev.command`
- **Windows:** `scripts/run-dev.ps1`

On macOS, a self-contained ad-hoc-signed bundle (needed to exercise the Desktop/Documents/Downloads file pickers) comes from `scripts/rebuild.command`; the Windows equivalent is `scripts/rebuild.ps1`.

## License

MIT © 2026 Yoshinao Inoguchi

## Contact

Yoshinao Inoguchi — nao7sep@gmail.com
