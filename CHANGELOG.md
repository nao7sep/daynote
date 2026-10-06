# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Every file DayNote keeps records its format version: `format_version` as the first key of a `.daynote` file, `formatVersion` in `config.json` and `state.json`, and the SQLite user version in `records.sqlite3` and `backups.sqlite3`. A file that records none is handled like a damaged one: a binder does not open, a settings or view-state file is set aside, and a records or backup database is left alone for the session.
- A file written by a newer DayNote is never changed. Such a binder is not opened, and the message names it; an open binder that a newer DayNote rewrites is closed without saving. Such a settings file stops DayNote at launch with a message naming it. Such a `state.json` is ignored for the session, and such a records or backup database leaves records going to the fallback log file and backups unrecorded for the session.

### Changed

- A note's lifecycle is draft, discarded, verified, published and retired. Verified replaces Ready, and Retired replaces Expired.
- Each status keeps the time the note reached it, and a status that implies an earlier one fills that time too: a published note always has a verification time, and a retired one a publication time. Moving back clears the times the new status does not hold, and un-retiring restores the original publication time.
- Locking is a separate switch beside the status. A locked note's title, text and attachments cannot be edited; its status can still change and it can still be deleted. Publishing no longer locks a note, and editing no longer needs a move back to an earlier status.
- The `.daynote` file stores `verified_at` and `retired_at` in place of `ready_at` and `expired_at`, adds `discarded_at` and `locked`, and no longer reads the `ready`, `expired` or `checked` statuses.
- A binder with a missing, malformed or repeated id, an attachment that is not a plain file name, an unknown status, a time that is not a time, or status times its status contradicts no longer opens with those values replaced or dropped. The message names the binder, and the file is left as it is.
- When a quit cannot save the open binder, DayNote stays open and asks whether to retry the save or quit anyway, instead of only showing the error. Closing the question keeps DayNote open with the changes.

### Fixed

- Quitting, closing a binder or opening another one while DayNote was checking the open binder for outside changes, or asking about one, could do nothing. It now waits for the check, saves, and goes ahead.
- A note's modified time changes only when its title, text or attachments change. Changing its status, locking it, an edit undone before the save, and whitespace the save removes no longer move it.
- A note's modified time is the time of its last edit, not the time the edit was saved.
- A note or binder whose created or modified time is missing from the file takes another time the file records, rather than the time the file was opened. A note uses its own status times before its binder's.
- A binder's modified time is the time of the last edit to its notes. Status and lock changes, and edits undone before the save, no longer move it.
- A status time is never earlier than the note's creation, even after the computer's clock was set back.
- A save, autosave or quit that comes before DayNote has noticed that a newer DayNote rewrote the open binder no longer writes over it.
- Saving a binder or the settings keeps the file's permissions, extended attributes and Finder tags on macOS, and a save that would write the same bytes leaves the file untouched.
- Changing the text style, renaming, reordering or removing a binder in the list, and adding a binder to it, now say so when the settings cannot be saved, instead of looking done and coming undone at the next launch.
- Deleting a note or removing an attachment deletes the files only after the binder is saved without them, so a failed save no longer leaves the binder pointing at deleted files.
- An attachment is copied under a temporary name first, so a copy that fails part-way no longer leaves a partial file in the binder's assets folder.
- Locking a note while files are still being added to it no longer adds them.
- A quit no longer waits without end for a binder location that stops responding: after two seconds it asks whether to retry or quit anyway.

## [0.1.0] - 2026-07-08

### Added

- First public release.
