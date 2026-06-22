# DayNote

DayNote is a macOS-first (also Windows) desktop app for keeping many plain-text notes organized into **binders** — a binder is a `.daynote` file (its attachments live in a folder beside it, so move the two together), a note is a single dated text entry with optional file attachments. It is persistence-first: everything you write is meant to be kept, written atomically with the live files as the source of truth. It suits anyone who wants a fast, local, file-owned home for a lot of small notes; a daily-journal workflow is the intended direction, though today the model is the flat two-level one (binders contain notes, no date grouping yet). DayNote succeeds *quickdeck*, generalizing its flat panes into binders-of-notes.

**Status:** early development (0.x). Data formats and features may change without notice; no backward-compatibility guarantees before 1.0.

## Requirements

- **.NET 10** runtime.
- **macOS** (primary) or **Windows**.

## Features

- **Binders of plain-text notes** — many notes per `.daynote` file, each with a title and body.
- **Attachments** — associate files with a note; add by drag-and-drop, reorder in place.
- **Lifecycle status** — draft → checked → published → expired; non-draft notes lock read-only until set back to draft.
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
