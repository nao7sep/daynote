using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DayNote.Core.Time;

namespace DayNote.Desktop.Logging;

/// <summary>
/// DayNote's only logger: one JSON object per line, one file per process launch, kept indefinitely.
/// Hand-rolled on <see cref="System.Text.Json"/> + a lock + a <see cref="StreamWriter"/> (no logging
/// framework) so flush, the debug gate, redaction, and the console fallback all behave exactly as the
/// logging convention requires.
/// </summary>
/// <remarks>
/// <para>
/// The per-launch file is named <c>yyyymmdd-hhmmss-utc.log</c> from the launch instant and written
/// for the life of the process. <c>warn</c>/<c>error</c>/<c>debug</c> lines are flushed immediately so
/// the last lines before a crash reach disk; <c>info</c> lines may be buffered and are flushed on
/// <see cref="Dispose"/>. If the file cannot be opened or written the logger degrades to
/// <see cref="Console.Error"/> and keeps running — logging never crashes the app and never silently
/// swallows its own failure.
/// </para>
/// </remarks>
public sealed class JsonLinesLogger : IAppLogger, IDisposable
{
    /// <summary>
    /// Field names whose values are redacted before serialization (exact, case-insensitive). Seeded
    /// with the obvious secrets; DayNote logs none of these today, but the backstop is mandatory for
    /// the day an object that happens to carry one is logged.
    /// </summary>
    private static readonly IReadOnlySet<string> DeniedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "apiKey",
        "authorization",
        "token",
        "password",
        "secret",
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // One physical line per event; keep non-ASCII (file paths, Japanese titles) readable rather
        // than \u-escaped — still valid JSON in a UTF-8 file.
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private readonly TextWriter _writer;
    private readonly bool _isFallback;
    private readonly bool _debugEnabled;
    private bool _disposed;

    private JsonLinesLogger(TextWriter writer, bool isFallback, bool debugEnabled)
    {
        _writer = writer;
        _isFallback = isFallback;
        _debugEnabled = debugEnabled;
    }

    /// <summary>
    /// Opens the per-launch log file under <paramref name="logsDirectory"/> (created if missing). On
    /// failure, returns a logger that writes to <see cref="Console.Error"/> instead, after surfacing
    /// the reason there — the app keeps running either way.
    /// </summary>
    /// <param name="logsDirectory">The app's <c>logs/</c> directory.</param>
    /// <param name="debugEnabled">Whether <see cref="Debug"/> lines are emitted (off on end-user machines).</param>
    public static JsonLinesLogger Open(string logsDirectory, bool debugEnabled)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
            var path = Path.Combine(logsDirectory, DayNoteTime.FileStamp(DateTimeOffset.UtcNow) + ".log");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream) { AutoFlush = false };
            return new JsonLinesLogger(writer, isFallback: false, debugEnabled);
        }
        catch (Exception ex)
        {
            // Best effort, no new dependencies: say so, then log to stderr for the session.
            Console.Error.WriteLine($"[daynote] log file unavailable, falling back to stderr: {ex.Message}");
            return new JsonLinesLogger(Console.Error, isFallback: true, debugEnabled);
        }
    }

    public void Debug(string message, object? data = null, Exception? error = null)
    {
        // The firehose: never reaches an end-user disk, free in development.
        if (_debugEnabled)
        {
            Write("debug", message, data, error);
        }
    }

    public void Info(string message, object? data = null, Exception? error = null) =>
        Write("info", message, data, error);

    public void Warn(string message, object? data = null, Exception? error = null) =>
        Write("warn", message, data, error);

    public void Error(string message, object? data = null, Exception? error = null) =>
        Write("error", message, data, error);

    private void Write(string level, string message, object? data, Exception? error)
    {
        // Render outside the lock; serialize only the write itself.
        var line = SafeRender(level, message, data, error);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _writer.Write(line);
                _writer.Write('\n');

                // warn/error/debug must reach disk now; info may buffer (the fallback always flushes).
                if (_isFallback || level != "info")
                {
                    _writer.Flush();
                }
            }
            catch (Exception writeError)
            {
                FallBackToConsole(line, writeError);
            }
        }
    }

    /// <summary>
    /// Renders a log line that is guaranteed never to throw: the full structured line if it
    /// serializes; else a minimal line (envelope plus the render error); else — if even that fails on
    /// a pathological string, e.g. an unpaired UTF-16 surrogate that <see cref="System.Text.Json"/>
    /// rejects — a constant line built from nothing the caller supplied. This degradation ladder is
    /// what lets a logging call honour "never crashes the app" for any input.
    /// </summary>
    private static string SafeRender(string level, string message, object? data, Exception? error)
    {
        try
        {
            return Render(level, message, data, error);
        }
        catch (Exception renderError)
        {
            try
            {
                return MinimalLine(level, message, renderError);
            }
            catch
            {
                return ConstantLine(level);
            }
        }
    }

    private static string Render(string level, string message, object? data, Exception? error)
    {
        var root = new JsonObject
        {
            ["time"] = DayNoteTime.ToIso(DateTimeOffset.UtcNow),
            ["level"] = level,
            ["message"] = message,
        };

        if (data is not null)
        {
            var serialized = JsonSerializer.SerializeToNode(data, SerializerOptions);
            if (serialized is JsonObject fields)
            {
                foreach (var field in fields.ToArray())
                {
                    // The envelope is the fixed contract; a stray data field of the same name never
                    // shadows it. DeepClone re-parents the value cleanly into the new tree.
                    if (!root.ContainsKey(field.Key))
                    {
                        root[field.Key] = field.Value?.DeepClone();
                    }
                }
            }
            else if (serialized is not null)
            {
                // A non-object data value (a scalar or array) is preserved under a single field
                // rather than silently dropped.
                root["data"] = serialized;
            }
        }

        if (error is not null)
        {
            root["error"] = BuildError(error);
        }

        LogRedactor.Redact(root, DeniedKeys);
        return root.ToJsonString(SerializerOptions);
    }

    /// <summary>
    /// Full exception fidelity: type, message, stack, and the cause chain. An
    /// <see cref="AggregateException"/> fans its multiple inner exceptions out into a <c>causes</c>
    /// array; an ordinary wrapped exception nests its single <c>cause</c>.
    /// </summary>
    private static JsonObject BuildError(Exception error)
    {
        var node = new JsonObject
        {
            ["type"] = error.GetType().FullName,
            ["message"] = error.Message,
            ["stack"] = error.StackTrace,
        };

        if (error is AggregateException aggregate)
        {
            var causes = new JsonArray();
            foreach (var inner in aggregate.InnerExceptions)
            {
                causes.Add(BuildError(inner));
            }

            node["causes"] = causes;
        }
        else if (error.InnerException is { } cause)
        {
            node["cause"] = BuildError(cause);
        }

        return node;
    }

    private static string MinimalLine(string level, string message, Exception renderError)
    {
        var node = new JsonObject
        {
            ["time"] = DayNoteTime.ToIso(DateTimeOffset.UtcNow),
            ["level"] = level,
            ["message"] = message,
            ["logError"] = renderError.Message,
        };

        return node.ToJsonString(SerializerOptions);
    }

    private static string ConstantLine(string level) =>
        // `level` is always one of the four logger-owned literals, so this is valid JSON without any
        // escaping and depends on nothing the caller supplied that could be malformed.
        $"{{\"level\":\"{level}\",\"message\":\"[log entry could not be rendered]\"}}";

    private void FallBackToConsole(string line, Exception writeError)
    {
        // The file write failed mid-session: surface it and the dropped line, but never throw.
        try
        {
            Console.Error.WriteLine($"[daynote] log write failed: {writeError.Message}");
            Console.Error.WriteLine(line);
        }
        catch
        {
            // Nothing left to try; logging must not crash the app.
        }
    }

    /// <summary>Flushes any buffered <c>info</c> lines and closes the file.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _writer.Flush();
            }
            catch
            {
                // Best effort on the way out.
            }

            // The fallback writer is Console.Error, which the logger does not own.
            if (!_isFallback)
            {
                _writer.Dispose();
            }
        }
    }
}
