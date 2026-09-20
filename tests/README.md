# DayNote's areas, and the tests that stand for them

`dotnet test` runs this whole test project: at a few seconds it is already a fixed, balanced run, so
nothing selects a subset of it. DayNote has nothing paid, external, or heavy in the product, so there
is no separate full gate: `dotnet test` is it. The macOS-only cases inside these files skip themselves
elsewhere rather than changing which tests the run covers.

This file is the balance judgement the `tests-folder-conventions` require — which areas DayNote has,
and which tests stand for each — so a reader can tell what a green run covered, and an area with no
test standing for it is visible rather than merely absent. `DayNote.Tests/AreaMapTests.cs` holds every
path below to what is on disk.

Every test source file in the project is placed in exactly one area. The three files that are
infrastructure rather than tests — `DayNote.Tests/TestApp.cs`, `DayNote.Tests/PlatformFacts.cs`, and
`DayNote.Tests/Storage/AppPathsEnvironment.cs` — stand for no area of their own.

Paths are relative to this folder.

| Area | What it covers | Tests standing for it |
|---|---|---|
| The binder file format | The `.daynote` file itself: the writer's canonical TOML shape, a lossless read back of it, and the note lifecycle — draft, ready, published, expired — whose timestamps that file carries. | `DayNote.Tests/Toml/BinderTomlTests.cs`, `DayNote.Tests/Models/NoteLifecycleTests.cs` |
| Saving, and detecting an outside edit | The file-I/O edge every binder, config, and state write crosses: write-then-rename leaving no temp file behind, a binder round trip, and the content hash that decides a file changed under the app. | `DayNote.Tests/Storage/AtomicFileTests.cs`, `DayNote.Tests/Storage/BinderStoreTests.cs`, `DayNote.Tests/Identity/ContentHashTests.cs` |
| Attachments | Files copied into the binder's adjacent `-assets` folder: the ids that name their directories, the case-insensitive uniqueness that stops one copy clobbering another, and reordering a list by pointer or keyboard. | `DayNote.Tests/Identity/IdGeneratorTests.cs`, `DayNote.Tests/Storage/UniqueFileNameTests.cs`, `DayNote.Tests/Views/ListReorderTests.cs` |
| Storage paths and the single-instance lease | Where DayNote's own tree resolves — `DAYNOTE_HOME` or `~/.daynote`, never the working directory — how two spellings of one binder path compare, and the lease that stops a second launch opening the same root. | `DayNote.Tests/Storage/AppPathsTests.cs`, `DayNote.Tests/Storage/PathKeyTests.cs`, `DayNote.Tests/Services/SingleInstanceLeaseTests.cs` |
| Settings and window state | The two JSON files under the storage root and the dialog that edits one: first run versus a corrupt file, deep-copied drafts, save-gating validation, and the window placement restored on launch. | `DayNote.Tests/Storage/JsonStoreTests.cs`, `DayNote.Tests/Configuration/AppConfigTests.cs`, `DayNote.Tests/Configuration/AppStateTests.cs`, `DayNote.Tests/Configuration/SettingsValidatorTests.cs`, `DayNote.Tests/Views/SettingsDialogTests.cs` |
| Backup history | The write-through SQLite history appended after each managed-text save: byte-identical content, dedup of an unchanged re-save, and failing without ever breaking the live save. | `DayNote.Tests/Backup/BackupStoreTests.cs` |
| The editing session and autosave | Open and close, dirty tracking, the debounced autosave and its flush, the known-binders list, and the editor's own load-versus-edit and commit boundaries — the logic where data loss would hide. | `DayNote.Tests/ViewModels/MainWindowViewModelTests.cs`, `DayNote.Tests/ViewModels/EditorViewModelTests.cs` |
| Text handling and counting | What is stored and what is said about it: body normalization on every read and write, the grapheme-safe truncation that derives a list label, and the word, code-point, and X-weighted counts in the status bar. | `DayNote.Tests/Text/BodyCleanupTests.cs`, `DayNote.Tests/Text/TextCleanupTests.cs`, `DayNote.Tests/Text/CharacterCountTests.cs`, `DayNote.Tests/Text/TwitterTextTests.cs`, `DayNote.Tests/Text/TwitterTextConformanceTests.cs` |
| Text entry and IME | The one IME-aware text box: a real Enter submits while a composition does not, a live preedit guards the window accelerators, and on macOS text handed back from the background picker reaches the field that had focus, at its caret. | `DayNote.Tests/Controls/ComposingTextBoxTests.cs`, `DayNote.Tests/Views/BackgroundTextInputTests.cs` |
| The interface text, labels, and times | The strings the app puts on screen and the catalogues behind them: note-row labels, empty-state sentences, failure text that never leaks raw diagnostics, the shortcut catalogue every displayed binding reads from, and the displayed and stamped times. | `DayNote.Tests/ViewModels/NoteListItemViewModelTests.cs`, `DayNote.Tests/ViewModels/MainWindowEmptyStatesTests.cs`, `DayNote.Tests/ViewModels/FailurePresentationTests.cs`, `DayNote.Tests/Views/ShortcutCatalogTests.cs`, `DayNote.Tests/Time/DayNoteTimeTests.cs` |
| Window, theme, and fonts | The chrome: the derived minimum window size and the overflow below it, the light and dark brush sets with their contrast and repaint, shared control styling, and the bundled Inter actually resolving instead of a platform fallback. | `DayNote.Tests/Views/WindowMetricsTests.cs`, `DayNote.Tests/Views/WindowOverflowTests.cs`, `DayNote.Tests/ThemeResourcesTests.cs`, `DayNote.Tests/AppStylesTests.cs`, `DayNote.Tests/FontResolutionTests.cs`, `DayNote.Tests/UiFontTests.cs` |
| Menus, shortcuts, and dialogs | The macOS menu bar built once through AppKit and the keys that route through it, the shortcuts help modal, the layout every dialog shares, and the About dialog's inline link failure. | `DayNote.Tests/Views/MacMenuBarTests.cs`, `DayNote.Tests/Views/ShortcutsDialogTests.cs`, `DayNote.Tests/Views/DialogBaseLayoutTests.cs`, `DayNote.Tests/Views/AboutDialogTests.cs` |
| Diagnostic logging | The JSON-lines log: one object per line with its envelope, the exception and cause chain, level buffering, redaction of denied fields, and never throwing out of a log call. | `DayNote.Tests/Logging/JsonLinesLoggerTests.cs`, `DayNote.Tests/Logging/LogRedactorTests.cs` |
| Packaging and release | What ships: the Windows installer's dual-scope contract, the macOS bundle's contents and its excluded debug symbols, the packaged licence, and the one version every other artifact copies. | `DayNote.Tests/InstallerConfigurationTests.cs`, `DayNote.Tests/VersionConsistencyTests.cs` |
