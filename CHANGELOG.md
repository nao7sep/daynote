# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- A note's lifecycle is draft, discarded, verified, published and retired. Verified replaces Ready, and Retired replaces Expired.
- Each status keeps the time the note reached it, and a status that implies an earlier one fills that time too: a published note always has a verification time, and a retired one a publication time. Moving back clears the times the new status does not hold, and un-retiring restores the original publication time.
- Locking is a separate switch beside the status. A locked note's title, text and attachments cannot be edited; its status can still change and it can still be deleted. Publishing no longer locks a note, and editing no longer needs a move back to an earlier status.
- The `.daynote` file stores `verified_at` and `retired_at` in place of `ready_at` and `expired_at`, adds `discarded_at` and `locked`, and no longer reads the `ready`, `expired` or `checked` statuses.

### Fixed

- A note's modified time changes only when its title, text or attachments change. Changing its status, locking it, an edit undone before the save, and whitespace the save removes no longer move it.

## [0.1.0] - 2026-07-08

### Added

- First public release.
