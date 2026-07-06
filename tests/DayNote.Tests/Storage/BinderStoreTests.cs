using System;
using System.IO;
using System.Text;
using DayNote.Core.Backup;
using DayNote.Core.Identity;
using DayNote.Core.Models;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// The binder store is the file-I/O edge: it must round-trip a binder losslessly, record a
/// content-hash baseline, and detect a later external modification (the hash, never the mtime, is
/// the signal). These guarantees drive autosave conflict handling, so each runs against a throwaway
/// file in a temp directory.
/// </summary>
/// <remarks>
/// A binder Save goes through the atomic writer, which is the write-through data-backup hook, so
/// <c>DAYNOTE_HOME</c> is relocated to this test's throwaway directory to keep the store out of the
/// developer's real <c>~/.daynote/</c>. Joined to the AppPaths collection so that process-wide env var
/// never races; the store singleton is closed in teardown so it re-opens per throwaway root.
/// </remarks>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class BinderStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;
    private readonly string? _previousHome;
    private readonly BinderStore _store = new();

    public BinderStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daynote-store-tests-" + IdGenerator.New());
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "journal.daynote");

        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _directory);
    }

    [Fact]
    public void Save_then_load_round_trips_and_reports_a_matching_hash()
    {
        var saved = _store.Save(_path, Sample());

        var loaded = _store.Load(_path);

        Assert.Equal(saved.ContentHash, loaded.ContentHash);
        Assert.Single(loaded.Binder.Notes);
        Assert.Equal("hello body", loaded.Binder.Notes[0].Body);
    }

    [Fact]
    public void CheckExternalChange_reports_none_when_the_file_is_unchanged()
    {
        var saved = _store.Save(_path, Sample());

        Assert.Equal(ExternalChange.None, _store.CheckExternalChange(_path, saved.ContentHash));
    }

    [Fact]
    public void CheckExternalChange_reports_modified_after_an_external_edit()
    {
        var saved = _store.Save(_path, Sample());

        // Simulate an edit by another program: the bytes differ, so the hash differs, even though
        // the file was just rewritten within the same coarse filesystem timestamp tick.
        File.WriteAllText(_path, File.ReadAllText(_path, Encoding.UTF8) + "\n# external edit\n", Encoding.UTF8);

        Assert.Equal(ExternalChange.Modified, _store.CheckExternalChange(_path, saved.ContentHash));
    }

    [Fact]
    public void CheckExternalChange_reports_deleted_when_the_file_is_gone()
    {
        var saved = _store.Save(_path, Sample());
        File.Delete(_path);

        Assert.Equal(ExternalChange.Deleted, _store.CheckExternalChange(_path, saved.ContentHash));
    }

    [Fact]
    public void ResolveAttachments_builds_paths_under_the_note_assets_directory()
    {
        var note = new Note { Id = "n1" };
        note.Attachments.Add("photo.png");

        var resolved = ResolveOne(note);

        Assert.Equal("photo.png", resolved.FileName);
        Assert.Equal(Path.Combine(_directory, "journal-assets", "n1", "photo.png"), resolved.FullPath);
        Assert.True(resolved.IsImage);
    }

    private Attachment ResolveOne(Note note)
    {
        var list = BinderStore.ResolveAttachments(_path, note);
        return Assert.Single(list);
    }

    private static Binder Sample()
    {
        var binder = new Binder
        {
            Id = "nb1",
            Created = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
        };
        binder.Notes.Add(new Note
        {
            Id = "n1",
            Title = "Note",
            Created = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero),
            Body = "hello body",
        });
        return binder;
    }

    public void Dispose()
    {
        BackupStore.Close();
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
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
