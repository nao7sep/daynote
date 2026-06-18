using System;
using System.IO;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// The per-notebook lock is what lets multiple application instances run safely: one notebook can be
/// held by exactly one instance at a time, a second attempt is refused (so the UI can fall back to
/// read-only), releasing frees it for re-acquisition, and different notebooks never contend.
/// </summary>
[Collection(AppPathsEnvironment.CollectionName)]
public sealed class NotebookLockTests : IDisposable
{
    private readonly string _root;
    private readonly string? _previousHome;
    private readonly AppPaths _paths;

    public NotebookLockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "daynote-lock-tests-" + Guid.NewGuid().ToString("N"));

        // Relocation goes through DAYNOTE_HOME, the one supported seam — the same variable tests and
        // production use — rather than a test-only constructor.
        _previousHome = Environment.GetEnvironmentVariable(AppPaths.HomeEnvironmentVariable);
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _root);
        _paths = new AppPaths();
    }

    [Fact]
    public void Acquiring_an_unheld_notebook_succeeds()
    {
        using var locked = NotebookLock.TryAcquire(_paths, NotebookPath("journal"));

        Assert.NotNull(locked);
    }

    [Fact]
    public void A_second_acquire_is_refused_while_the_first_is_held()
    {
        var path = NotebookPath("journal");
        using var first = NotebookLock.TryAcquire(_paths, path);

        var second = NotebookLock.TryAcquire(_paths, path);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Releasing_allows_re_acquisition()
    {
        var path = NotebookPath("journal");
        var first = NotebookLock.TryAcquire(_paths, path);
        Assert.NotNull(first);
        first!.Dispose();

        using var second = NotebookLock.TryAcquire(_paths, path);

        Assert.NotNull(second);
    }

    [Fact]
    public void Different_notebooks_can_be_held_at_the_same_time()
    {
        using var one = NotebookLock.TryAcquire(_paths, NotebookPath("one"));
        using var two = NotebookLock.TryAcquire(_paths, NotebookPath("two"));

        Assert.NotNull(one);
        Assert.NotNull(two);
    }

    [Fact]
    public void Paths_differing_only_in_case_share_one_lock()
    {
        using var lower = NotebookLock.TryAcquire(_paths, NotebookPath("Journal"));

        var upper = NotebookLock.TryAcquire(_paths, NotebookPath("JOURNAL"));

        Assert.NotNull(lower);
        Assert.Null(upper);
    }

    private string NotebookPath(string name) => Path.Combine(_root, "books", name + ".daynote");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppPaths.HomeEnvironmentVariable, _previousHome);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp directory is harmless.
        }
    }
}
