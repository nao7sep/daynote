using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DayNote.Core.Backup;
using DayNote.Core.Configuration;
using DayNote.Core.Models;
using DayNote.Core.Storage;
using DayNote.Tests.Storage;
using Xunit;

namespace DayNote.Tests.Backup;

/// <summary>
/// End-to-end backup runs over a throwaway <c>DAYNOTE_HOME</c>: a first run captures config, a binder, and
/// its assets at the mirror paths; an unchanged run writes nothing; an edit captures only what changed; a
/// corrupt index resets to a full backup; a dead binder link is skipped without failing the run.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class BackupEngineTests : IDisposable
{
    private static readonly DateTimeOffset Run1 = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Run2 = new(2026, 7, 1, 1, 0, 0, TimeSpan.Zero);

    private readonly string? _previousHome;
    private readonly string _home;
    private readonly string _docs;
    private readonly AppPaths _paths;

    public BackupEngineTests()
    {
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        _home = CreateTempDir("daynote-home-");
        _docs = CreateTempDir("daynote-docs-");
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _home);
        _paths = new AppPaths();
        Directory.CreateDirectory(_paths.Root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);
        TryDeleteDir(_home);
        TryDeleteDir(_docs);
    }

    [Fact]
    public void First_Run_Captures_Config_Binder_And_Assets()
    {
        WriteConfig("{\"a\":1}");
        CreateBinder("Journal", "BINDER1", ("note1", "photo.txt", "hello"));

        var report = new BackupEngine(_paths).Run(Run1);

        Assert.Null(report.Fatal);
        Assert.False(report.NothingChanged);
        Assert.Equal(3, report.FilesArchived);
        Assert.Equal("backup-20260701-000000-utc.zip", report.ArchiveFileName);

        var entries = ArchiveEntries("backup-20260701-000000-utc.zip");
        Assert.Contains("config.json", entries);
        Assert.Contains("binders/BINDER1/Journal.daynote", entries);
        Assert.Contains("binders/BINDER1/assets/note1/photo.txt", entries);

        var index = LoadIndex();
        Assert.Equal(3, index.Entries.Count);
        Assert.All(index.Entries, e => Assert.Equal("20260701-000000-utc", e.ArchivedAt));
    }

    [Fact]
    public void Second_Run_With_No_Changes_Writes_Nothing()
    {
        WriteConfig("{\"a\":1}");
        CreateBinder("Journal", "BINDER1", ("note1", "photo.txt", "hello"));

        new BackupEngine(_paths).Run(Run1);
        var report = new BackupEngine(_paths).Run(Run2);

        Assert.True(report.NothingChanged);
        Assert.Null(report.ArchiveFileName);
        Assert.False(File.Exists(Path.Combine(_paths.BackupsDirectory, "backup-20260701-010000-utc.zip")));
    }

    [Fact]
    public void An_Edit_Captures_Only_The_Changed_File()
    {
        WriteConfig("{\"a\":1}");
        CreateBinder("Journal", "BINDER1", ("note1", "photo.txt", "hello"));
        new BackupEngine(_paths).Run(Run1);

        WriteConfig("{\"a\":1,\"b\":2}"); // larger, so size differs and the change is caught regardless of mtime

        var report = new BackupEngine(_paths).Run(Run2);

        Assert.False(report.NothingChanged);
        Assert.Equal(1, report.FilesArchived);
        Assert.Equal(new[] { "config.json" }, ArchiveEntries("backup-20260701-010000-utc.zip"));
    }

    [Fact]
    public void A_Corrupt_Index_Is_Reset_And_Everything_Is_Recaptured()
    {
        WriteConfig("{\"a\":1}");
        CreateBinder("Journal", "BINDER1", ("note1", "photo.txt", "hello"));
        new BackupEngine(_paths).Run(Run1);

        File.WriteAllText(_paths.BackupIndexFile, "{ this is not valid json");

        var report = new BackupEngine(_paths).Run(Run2);

        Assert.True(report.IndexWasReset);
        Assert.Equal(3, report.FilesArchived);
    }

    [Fact]
    public void A_Dead_Binder_Link_Is_Skipped_And_The_Run_Continues()
    {
        WriteConfig("{\"a\":1}");
        var missing = Path.Combine(_docs, "Gone.daynote");
        WriteState(new KnownBinder { Path = missing, Title = "Gone" });

        var report = new BackupEngine(_paths).Run(Run1);

        Assert.False(report.NothingChanged);   // config.json is still captured
        Assert.Equal(1, report.FilesArchived);
        Assert.Contains(report.Skips, s => s.Path == missing);
    }

    // --- helpers ---

    private void WriteConfig(string json) => File.WriteAllText(_paths.ConfigFile, json);

    private void WriteState(params KnownBinder[] binders)
    {
        var state = new AppState();
        state.Binders.AddRange(binders);
        new JsonStore<AppState>(_paths.StateFile).Save(state);
    }

    private void CreateBinder(string baseName, string id, (string noteId, string file, string content) asset)
    {
        var binderPath = Path.Combine(_docs, baseName + ".daynote");
        new BinderStore().Save(binderPath, new Binder { Id = id, Created = Run1, Modified = Run1 });

        var noteDir = Path.Combine(BinderStore.AssetsDirectory(binderPath), asset.noteId);
        Directory.CreateDirectory(noteDir);
        File.WriteAllText(Path.Combine(noteDir, asset.file), asset.content);

        WriteState(new KnownBinder { Path = binderPath, Title = baseName });
    }

    private BackupIndex LoadIndex() =>
        new JsonStore<BackupIndex>(_paths.BackupIndexFile).Load() ?? new BackupIndex();

    private string[] ArchiveEntries(string archiveName)
    {
        using var zip = ZipFile.OpenRead(Path.Combine(_paths.BackupsDirectory, archiveName));
        return zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    private static string CreateTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
