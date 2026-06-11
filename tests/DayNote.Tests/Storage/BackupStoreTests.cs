using System;
using System.IO;
using DayNote.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// The backup store is the recovery net, so its three guarantees matter: identical content is
/// deduplicated, recording is throttled per interval, and a forced close still captures the latest
/// state. Each test runs against a throwaway SQLite database in a temp directory.
/// </summary>
public sealed class BackupStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 11, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly string _directory;
    private readonly BackupStore _store;

    public BackupStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daynote-backup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new BackupStore(Path.Combine(_directory, "backups.sqlite"));
    }

    [Fact]
    public void Recording_new_content_inserts_a_version()
    {
        Assert.Equal(BackupOutcome.Inserted, _store.Record("key", "A", T0, Minute, force: false));
        Assert.Single(_store.List("key"));
    }

    [Fact]
    public void Identical_content_is_deduplicated_even_when_forced()
    {
        _store.Record("key", "A", T0, Minute, force: false);

        Assert.Equal(BackupOutcome.Duplicate, _store.Record("key", "A", T0.AddSeconds(1), Minute, force: false));
        Assert.Equal(BackupOutcome.Duplicate, _store.Record("key", "A", T0.AddMinutes(5), Minute, force: true));
        Assert.Single(_store.List("key"));
    }

    [Fact]
    public void A_second_version_inside_the_throttle_window_is_skipped()
    {
        _store.Record("key", "A", T0, Minute, force: false);

        Assert.Equal(BackupOutcome.Throttled, _store.Record("key", "B", T0.AddSeconds(10), Minute, force: false));
        Assert.Single(_store.List("key"));
    }

    [Fact]
    public void Force_bypasses_the_throttle()
    {
        _store.Record("key", "A", T0, Minute, force: false);

        Assert.Equal(BackupOutcome.Inserted, _store.Record("key", "B", T0.AddSeconds(10), Minute, force: true));
        Assert.Equal(2, _store.List("key").Count);
    }

    [Fact]
    public void A_version_after_the_throttle_window_is_recorded()
    {
        _store.Record("key", "A", T0, Minute, force: false);

        Assert.Equal(BackupOutcome.Inserted, _store.Record("key", "B", T0.AddSeconds(61), Minute, force: false));
        Assert.Equal(2, _store.List("key").Count);
    }

    [Fact]
    public void Get_content_returns_the_exact_stored_text_and_null_for_an_unknown_id()
    {
        _store.Record("key", "the exact bytes", T0, Minute, force: false);
        var version = Assert.Single(_store.List("key"));

        Assert.Equal("the exact bytes", _store.GetContent(version.Id));
        Assert.Null(_store.GetContent("does-not-exist"));
    }

    [Fact]
    public void Versions_are_isolated_per_notebook_key()
    {
        _store.Record("one", "A", T0, Minute, force: false);
        _store.Record("two", "B", T0, Minute, force: false);

        var one = Assert.Single(_store.List("one"));
        Assert.Equal("A", _store.GetContent(one.Id));
        Assert.Single(_store.List("two"));
    }

    [Fact]
    public void List_returns_versions_newest_first()
    {
        _store.Record("key", "A", T0, Minute, force: true);
        _store.Record("key", "B", T0.AddSeconds(1), Minute, force: true);
        _store.Record("key", "C", T0.AddSeconds(2), Minute, force: true);

        var versions = _store.List("key");

        Assert.Equal(3, versions.Count);
        Assert.True(versions[0].CreatedUtc > versions[1].CreatedUtc);
        Assert.True(versions[1].CreatedUtc > versions[2].CreatedUtc);
        Assert.Equal("C", _store.GetContent(versions[0].Id));
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections, which can keep the file handle open; clear the
        // pool before deleting so the temp directory can be removed cleanly.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory is harmless.
        }
    }
}
