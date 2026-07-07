namespace DayNote.Logging;

/// <summary>
/// DayNote's application logger: a structured-object-in, JSON-Lines sink. Callers describe
/// <em>what happened</em> as a short, stable <paramref name="message"/> plus an optional anonymous
/// <c>data</c> object whose properties become free fields on the log line; the logger owns the
/// envelope (<c>time</c>/<c>level</c>/<c>message</c>), redaction, serialization, and flushing.
/// </summary>
/// <remarks>
/// All four levels accept the same shape so a caught exception can be attached at whatever level is
/// appropriate (a recovered failure at <c>warn</c>, a transient one at <c>debug</c>, a real one at
/// <c>error</c>). Pass <paramref name="data"/> for the common case; name <c>error</c> when there is
/// no data to summarize.
/// </remarks>
public interface IAppLogger
{
    /// <summary>Developer-only detail. Emitted only when debug logging is enabled (dev build or <c>DAYNOTE_DEBUG=1</c>).</summary>
    void Debug(string message, object? data = null, Exception? error = null);

    /// <summary>A meaningful event of normal operation. Always written.</summary>
    void Info(string message, object? data = null, Exception? error = null);

    /// <summary>Something unexpected the app recovered from or chose to continue past. Always written.</summary>
    void Warn(string message, object? data = null, Exception? error = null);

    /// <summary>Something the app could not handle as normal operation. Always written.</summary>
    void Error(string message, object? data = null, Exception? error = null);
}
