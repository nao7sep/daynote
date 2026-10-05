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

### Fixed

- A note's modified time changes only when its title, text or attachments change. Changing its status, locking it, an edit undone before the save, and whitespace the save removes no longer move it.
- A note's modified time is the time of its last edit, not the time the edit was saved.
- A note or binder whose created or modified time is missing from the file takes another time the file records, rather than the time the file was opened.

## [0.1.0] - 2026-07-08

### Added

- First public release.
