using System;
using System.IO;
using System.Text;
using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Every binder, config, and state write goes through the atomic writer, so its guarantees matter:
/// the target ends up with exactly the supplied content (UTF-8, no BOM), an existing file is replaced
/// in full, and the temp file used for the write-then-rename is never left behind.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public AtomicFileTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daynote-atomic-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "data.txt");
    }

    [Fact]
    public void Writes_the_exact_content()
    {
        AtomicFile.WriteAllText(_path, "line one\nline two\n");

        Assert.Equal("line one\nline two\n", File.ReadAllText(_path, Encoding.UTF8));
    }

    [Fact]
    public void Overwrites_an_existing_file_in_full()
    {
        AtomicFile.WriteAllText(_path, "a much longer original content");
        AtomicFile.WriteAllText(_path, "short");

        Assert.Equal("short", File.ReadAllText(_path, Encoding.UTF8));
    }

    [Fact]
    public void Writes_utf8_without_a_byte_order_mark()
    {
        AtomicFile.WriteAllText(_path, "日本語");

        var bytes = File.ReadAllBytes(_path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("日本語", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Leaves_no_temp_file_behind()
    {
        AtomicFile.WriteAllText(_path, "content");

        Assert.Equal(new[] { _path }, Directory.GetFiles(_directory));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Creates_missing_parent_directories()
    {
        var nested = Path.Combine(_directory, "a", "b", "data.txt");

        AtomicFile.WriteAllText(nested, "deep");

        Assert.Equal("deep", File.ReadAllText(nested, Encoding.UTF8));
    }

    public void Dispose()
    {
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
