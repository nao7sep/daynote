# DayNote

DayNote is a local-first desktop app for keeping plain-text notes in **binders**. Each binder is a `.daynote` file you choose where to save; copied attachments live in a matching `-assets` folder beside it. The format is easy to move or back up as ordinary files on macOS and Windows.

DayNote is for short journal entries, working notes, and other text you want to own as files instead of placing in a hosted account. Binders contain a flat list of notes rather than calendar or date-grouped pages. Saves are atomic, external edits are detected, and a failed autosave is shown while the in-memory edit remains available to retry.

## Download

Prebuilt builds for **macOS (Apple Silicon)** and **Windows (x64)** are on the [Releases](https://github.com/nao7sep/daynote/releases/latest) page — a `.dmg` / `setup.exe` installer or a portable `.zip`. The builds are **self-contained** (no .NET install needed) and **unsigned**, so the OS warns the first time you open one:

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

## Files and recovery

Move or back up a binder together with its adjacent `<binder-name>-assets` folder. Adding an attachment copies the file into that folder; removing an attachment deletes the copied file after confirmation. Deleting a note leaves its attachment files on disk.

DayNote also appends each changed managed-text save to `~/.daynote/backups.sqlite3` after the live file has been written. This history includes binder text and DayNote's configuration and state, but not binary attachments. There is no in-app restore browser, and a backup-store failure never blocks the live save, so this history is a recovery aid rather than a substitute for your normal file backups.

## Run from source

The fastest way to try it:

- **macOS:** `scripts/run-dev.command`
- **Windows:** `scripts/run-dev.ps1`

On macOS, a self-contained ad-hoc-signed bundle (needed to exercise the Desktop/Documents/Downloads file pickers) comes from `scripts/rebuild.command`; the Windows equivalent is `scripts/rebuild.ps1`.

## License

[GNU GPL v3 or later](LICENSE) © 2026 Yoshinao Inoguchi

## Contact

Yoshinao Inoguchi — yoshinao@inoguchi.com — <https://inoguchi.com>
