using System.Text.Json;
using System.Text.Json.Serialization;

namespace DayNote.Core.Configuration;

/// <summary>
/// Volatile session state, persisted to <c>~/.daynote/state.json</c>: pane widths, the known-binders
/// list, and the current selection. Kept separate from <see cref="AppConfig"/> so durable preferences
/// and throwaway session state do not mix. Main-window placement is another disposable view-state
/// role and is kept here rather than in durable configuration.
/// </summary>
public sealed class AppState
{
    // The pixel width the user last dragged each side pane to (the "intent"). The editor pane is the
    // fill column (star-sized) and is not persisted — it absorbs whatever space remains. On restore
    // each intent is clamped to [MinWidth, whatFitsTheCurrentWindow] so a stale value can never
    // reopen a pane below its minimum or push the editor below its own minimum.
    public double BindersPaneWidth { get; set; } = 220;
    public double NotesPaneWidth { get; set; } = 260;
    public double AttachmentsPaneWidth { get; set; } = 260;

    // Known binders, each a file path plus its locally-stored display title. Not capped — the user
    // prunes the list explicitly via the row ✕, so a known binder never silently disappears. The title
    // lives here (per machine), not in the .daynote file: a binder is a collection, and a collection's
    // title is a local label, so it is intentionally not carried with the file to other computers.
    // (Renamed from the earlier string-only RecentBinders; the old key is simply ignored on load.)
    public List<KnownBinder> Binders { get; set; } = new();

    // Current selection, restored on next launch.
    public string? CurrentBinderPath { get; set; }
    public string? CurrentNoteId { get; set; }

    [JsonConverter(typeof(WindowPlacementsConverter))]
    public WindowPlacements WindowPlacements { get; set; } = new();
}

public sealed class WindowPlacements
{
    public WindowPlacement? Main { get; set; }
}

public sealed class WindowPlacement
{
    public WindowBounds? NormalBounds { get; set; }
    public string Mode { get; set; } = "maximized";
}

public sealed class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class WindowPlacementsConverter : JsonConverter<WindowPlacements>
{
    public override WindowPlacements Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("main", out var main)
            || main.ValueKind != JsonValueKind.Object)
            return new WindowPlacements();

        var mode = main.TryGetProperty("mode", out var modeValue)
            && modeValue.ValueKind == JsonValueKind.String
            && modeValue.GetString() is "normal" or "maximized"
                ? modeValue.GetString()!
                : "maximized";
        WindowBounds? bounds = null;
        if (main.TryGetProperty("normalBounds", out var normal)
            && normal.ValueKind == JsonValueKind.Object
            && TryInt(normal, "x", out var x)
            && TryInt(normal, "y", out var y)
            && TryInt(normal, "width", out var width)
            && TryInt(normal, "height", out var height))
            bounds = new WindowBounds { X = x, Y = y, Width = width, Height = height };
        return new WindowPlacements { Main = new WindowPlacement { NormalBounds = bounds, Mode = mode } };
    }

    public override void Write(Utf8JsonWriter writer, WindowPlacements value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("main");
        JsonSerializer.Serialize(writer, value.Main, options);
        writer.WriteEndObject();
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }
}

public sealed record DisplayWorkArea(int X, int Y, int Width, int Height, double Scaling);

public static class WindowPlacementPolicy
{
    public static WindowPlacement Resolve(
        WindowPlacement? saved,
        double minimumWidth,
        double minimumHeight,
        IEnumerable<DisplayWorkArea> displays,
        string defaultMode = "maximized")
    {
        var mode = saved?.Mode is "normal" or "maximized" ? saved.Mode : defaultMode;
        var bounds = saved?.NormalBounds;
        return new WindowPlacement
        {
            Mode = mode,
            NormalBounds = bounds is not null && IsUsable(bounds, minimumWidth, minimumHeight, displays)
                ? bounds
                : null,
        };
    }

    public static bool IsUsable(
        WindowBounds bounds,
        double minimumWidth,
        double minimumHeight,
        IEnumerable<DisplayWorkArea> displays) => displays.Any(display =>
            bounds.Width >= Math.Ceiling(minimumWidth * display.Scaling)
            && bounds.Height >= Math.Ceiling(minimumHeight * display.Scaling)
            && bounds.X >= display.X
            && bounds.Y >= display.Y
            && (long)bounds.X + bounds.Width <= (long)display.X + display.Width
            && (long)bounds.Y + bounds.Height <= (long)display.Y + display.Height);
}

/// <summary>A binder the user has opened: its file path and the locally-stored display title.</summary>
public sealed class KnownBinder
{
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
