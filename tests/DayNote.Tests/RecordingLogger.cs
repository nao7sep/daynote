using System;
using System.Collections.Generic;
using DayNote.Logging;

namespace DayNote.Tests;

/// <summary>A logger that keeps what it is told, for a test to read back.</summary>
internal sealed class RecordingLogger : IAppLogger
{
    public List<(string Level, string Message)> Entries { get; } = [];

    public void Debug(string message, object? data = null, Exception? error = null) => Entries.Add(("debug", message));
    public void Info(string message, object? data = null, Exception? error = null) => Entries.Add(("info", message));
    public void Warn(string message, object? data = null, Exception? error = null) => Entries.Add(("warn", message));
    public void Error(string message, object? data = null, Exception? error = null) => Entries.Add(("error", message));
}
