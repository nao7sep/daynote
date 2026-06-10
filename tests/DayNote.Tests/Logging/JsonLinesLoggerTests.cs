using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DayNote.Desktop.Logging;
using Xunit;

namespace DayNote.Tests.Logging;

public sealed class JsonLinesLoggerTests
{
    /// <summary>A throwaway directory that is removed when the test finishes.</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daynote-logtests", Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best effort; a leftover temp dir must not fail the test.
            }
        }
    }

    // The logger keeps the file open for writing (FileShare.Read), so a concurrent reader must
    // tolerate the live write handle by sharing read+write.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonNode[] ReadLines(string dir)
    {
        var file = Directory.GetFiles(dir, "*.log").Single();
        return ReadLinesFromFile(file);
    }

    private static JsonNode[] ReadLinesFromFile(string file)
    {
        return ReadShared(file)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!)
            .ToArray();
    }

    [Fact]
    public void Names_the_file_with_the_utc_per_launch_convention()
    {
        using var temp = new TempDir();
        using (JsonLinesLogger.Open(temp.Path, debugEnabled: false))
        {
        }

        var name = Path.GetFileName(Directory.GetFiles(temp.Path, "*.log").Single());
        Assert.Matches(@"^\d{8}-\d{6}-utc\.log$", name);
    }

    [Fact]
    public void Writes_one_json_object_per_line_with_the_envelope_and_free_fields()
    {
        using var temp = new TempDir();
        using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false))
        {
            log.Info("Notebook opened", new { path = "/tmp/x.daynote", noteCount = 3 });
        }

        var line = Assert.Single(ReadLines(temp.Path));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", (string?)line["time"]);
        Assert.Equal("info", (string?)line["level"]);
        Assert.Equal("Notebook opened", (string?)line["message"]);
        Assert.Equal("/tmp/x.daynote", (string?)line["path"]);
        Assert.Equal(3, (int)line["noteCount"]!);
    }

    [Fact]
    public void Redacts_denied_fields_carried_in_the_data_object()
    {
        using var temp = new TempDir();
        using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false))
        {
            log.Info("config", new { password = "hunter2", displayTimeZone = "Asia/Tokyo" });
        }

        var line = Assert.Single(ReadLines(temp.Path));
        Assert.Equal(LogRedactor.Marker, (string?)line["password"]);
        Assert.Equal("Asia/Tokyo", (string?)line["displayTimeZone"]);
    }

    [Fact]
    public void Captures_the_exception_type_message_stack_and_cause_chain()
    {
        using var temp = new TempDir();
        Exception captured;
        try
        {
            try
            {
                throw new IOException("disk gone");
            }
            catch (Exception inner)
            {
                throw new InvalidOperationException("save failed", inner);
            }
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false))
        {
            log.Error("Failed to save notebook", new { path = "/tmp/x" }, captured);
        }

        var error = Assert.Single(ReadLines(temp.Path))["error"]!;
        Assert.Equal(typeof(InvalidOperationException).FullName, (string?)error["type"]);
        Assert.Equal("save failed", (string?)error["message"]);
        Assert.False(string.IsNullOrEmpty((string?)error["stack"]));

        var cause = error["cause"]!;
        Assert.Equal(typeof(IOException).FullName, (string?)cause["type"]);
        Assert.Equal("disk gone", (string?)cause["message"]);
    }

    [Fact]
    public void Debug_is_suppressed_when_disabled_and_emitted_when_enabled()
    {
        using (var temp = new TempDir())
        {
            using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false))
            {
                log.Debug("diagnostic");
                log.Info("normal");
            }

            var levels = ReadLines(temp.Path).Select(n => (string?)n["level"]).ToArray();
            Assert.DoesNotContain("debug", levels);
            Assert.Contains("info", levels);
        }

        using (var temp = new TempDir())
        {
            using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: true))
            {
                log.Debug("diagnostic");
            }

            Assert.Equal("debug", (string?)Assert.Single(ReadLines(temp.Path))["level"]);
        }
    }

    [Fact]
    public void Flushes_warn_immediately_but_buffers_info_until_dispose()
    {
        using var temp = new TempDir();

        // info alone is buffered: nothing is on disk before flush/dispose.
        using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false))
        {
            log.Info("buffered");
            Assert.Empty(ReadLines(temp.Path));

            // warn must reach disk immediately, without disposing.
            log.Warn("now");
            var levels = ReadLines(temp.Path).Select(n => (string?)n["level"]).ToArray();
            Assert.Contains("warn", levels);
        }

        // After dispose the buffered info line is flushed too.
        Assert.Contains("info", ReadLines(temp.Path).Select(n => (string?)n["level"]));
    }

    [Fact]
    public void Never_throws_when_the_log_file_cannot_be_opened()
    {
        using var temp = new TempDir();
        // A file sits where the logger wants a directory, so opening the log fails and the logger
        // must degrade to the console fallback rather than crash.
        var collision = Path.Combine(temp.Path, "logs-but-a-file");
        File.WriteAllText(collision, "occupied");

        var ex = Record.Exception(() =>
        {
            using var log = JsonLinesLogger.Open(collision, debugEnabled: true);
            log.Info("info");
            log.Warn("warn");
            log.Error("error", error: new InvalidOperationException("boom"));
            log.Debug("debug");
        });

        Assert.Null(ex);
    }

    [Fact]
    public void Fans_an_aggregate_exception_into_a_causes_array_keeping_every_inner()
    {
        using var temp = new TempDir();
        // The unobserved-task crash hook hands the logger an AggregateException; every concurrent
        // fault must survive, not just the first.
        var aggregate = new AggregateException(
            new IOException("disk one"),
            new InvalidOperationException("bad state two"),
            new TimeoutException("slow three"));

        using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false))
        {
            log.Error("Unobserved task exception", error: aggregate);
        }

        var error = Assert.Single(ReadLines(temp.Path))["error"]!;
        Assert.Equal(typeof(AggregateException).FullName, (string?)error["type"]);
        Assert.Null(error["cause"]); // an aggregate uses `causes`, never the singular `cause`

        var causes = error["causes"]!.AsArray();
        Assert.Equal(3, causes.Count);
        Assert.Equal("disk one", (string?)causes[0]!["message"]);
        Assert.Equal("bad state two", (string?)causes[1]!["message"]);
        Assert.Equal("slow three", (string?)causes[2]!["message"]);
    }

    [Theory]
    [InlineData(42)]
    [InlineData("a bare string")]
    public void Preserves_a_non_object_data_value_under_a_data_field(object scalar)
    {
        using var temp = new TempDir();
        using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false))
        {
            log.Info("odd payload", scalar);
        }

        var line = Assert.Single(ReadLines(temp.Path));
        // The value is kept (not silently dropped), nested under a single `data` field.
        Assert.Equal(scalar.ToString(), line["data"]!.ToString());
        Assert.Equal("odd payload", (string?)line["message"]);
    }

    [Fact]
    public void Falls_back_to_a_minimal_line_when_the_data_object_cannot_serialize()
    {
        using var temp = new TempDir();
        using (var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false))
        {
            // Serializing this throws (the property getter throws), so rendering degrades to the
            // minimal line — the event is still recorded, with the render error captured.
            log.Info("event survived", new ExplodingData());
        }

        var line = Assert.Single(ReadLines(temp.Path));
        Assert.Equal("info", (string?)line["level"]);
        Assert.Equal("event survived", (string?)line["message"]);
        Assert.False(string.IsNullOrEmpty((string?)line["logError"]));
    }

    [Fact]
    public void Never_throws_and_still_writes_a_line_for_a_pathological_message()
    {
        using var temp = new TempDir();
        // A lone UTF-16 surrogate is invalid; System.Text.Json may reject it. The logger must still
        // not throw and must still emit a parseable line (degrading to the constant last resort).
        var ex = Record.Exception(() =>
        {
            using var log = JsonLinesLogger.Open(temp.Path, debugEnabled: false);
            log.Info("\uD800");
        });

        Assert.Null(ex);
        var line = Assert.Single(ReadLines(temp.Path));
        Assert.Equal("info", (string?)line["level"]);
    }

    /// <summary>A data object whose serialization always throws, to exercise the render fallback.</summary>
    private sealed class ExplodingData
    {
        public string Boom => throw new InvalidOperationException("getter exploded");
    }
}
