using DayNote.Core.Storage;

namespace DayNote.Tests.Storage;

/// <summary>Identity headers for hand-written fixtures (file-identity-conventions), as DayNote writes them.</summary>
internal static class TestHeaders
{
    private const string Writer = "\"writer\":{\"name\":\"DayNote\",\"version\":\"1.0.0\"}";

    /// <summary>The start of a JSON file through the header and the comma after it.</summary>
    public const string ConfigOpen = "{\"format\":{\"owner\":\"daynote\",\"kind\":\"config\",\"version\":1}," + Writer + ",";
    public const string BinderListOpen = "{\"format\":{\"owner\":\"daynote\",\"kind\":\"binderList\",\"version\":1}," + Writer + ",";
    public const string StateOpen = "{\"format\":{\"owner\":\"daynote\",\"kind\":\"state\",\"version\":1}," + Writer + ",";

    /// <summary>A binder's first two lines.</summary>
    public const string BinderToml =
        "format = { owner = \"daynote\", kind = \"binder\", version = 1 }\nwriter = { name = \"DayNote\", version = \"1.0.0\" }\n";

    public static string JsonOpen(FileFormat format, long version) =>
        $"{{\"format\":{{\"owner\":\"daynote\",\"kind\":\"{format.Kind}\",\"version\":{version}}},{Writer},";

    public static string BinderTomlAt(long version) =>
        $"format = {{ owner = \"daynote\", kind = \"binder\", version = {version} }}\nwriter = {{ name = \"DayNote\", version = \"1.0.0\" }}\n";
}
