using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using DayNote.Desktop.Logging;
using Xunit;

namespace DayNote.Tests.Logging;

public sealed class LogRedactorTests
{
    private static readonly IReadOnlySet<string> Denied =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "token", "password", "apiKey" };

    private static JsonObject Redacted(JsonObject root)
    {
        LogRedactor.Redact(root, Denied);
        return root;
    }

    [Fact]
    public void Replaces_the_value_of_a_denied_field_with_the_marker()
    {
        var root = Redacted(new JsonObject { ["token"] = "abc123", ["count"] = 5 });

        Assert.Equal(LogRedactor.Marker, (string?)root["token"]);
        Assert.Equal(5, (int)root["count"]!);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("Token")]
    [InlineData("TOKEN")]
    [InlineData("ApiKey")]
    [InlineData("APIKEY")]
    public void Matches_field_names_case_insensitively(string field)
    {
        var root = Redacted(new JsonObject { [field] = "value" });

        Assert.Equal(LogRedactor.Marker, (string?)root[field]);
    }

    [Theory]
    [InlineData("tokenCount")]
    [InlineData("broken")]
    [InlineData("accessToken")]
    [InlineData("passwords")]
    public void Never_matches_a_substring_of_a_denied_key(string field)
    {
        var root = Redacted(new JsonObject { [field] = "value" });

        Assert.Equal("value", (string?)root[field]);
    }

    [Fact]
    public void Recurses_through_nested_objects()
    {
        var root = Redacted(new JsonObject
        {
            ["outer"] = new JsonObject { ["password"] = "hunter2", ["keep"] = "ok" },
        });

        var outer = root["outer"]!.AsObject();
        Assert.Equal(LogRedactor.Marker, (string?)outer["password"]);
        Assert.Equal("ok", (string?)outer["keep"]);
    }

    [Fact]
    public void Recurses_through_arrays_of_objects()
    {
        var root = Redacted(new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject { ["token"] = "a", ["id"] = 1 },
                new JsonObject { ["token"] = "b", ["id"] = 2 }),
        });

        var items = root["items"]!.AsArray();
        Assert.Equal(LogRedactor.Marker, (string?)items[0]!["token"]);
        Assert.Equal(LogRedactor.Marker, (string?)items[1]!["token"]);
        Assert.Equal(1, (int)items[0]!["id"]!);
        Assert.Equal(2, (int)items[1]!["id"]!);
    }

    [Fact]
    public void Leaves_every_non_matched_field_byte_identical()
    {
        // A "message" that merely contains a secret-like word is prose, not a denied field name,
        // so the redactor — which matches names, never values — must not touch it.
        var root = new JsonObject
        {
            ["message"] = "user supplied token=abc in the form",
            ["path"] = "/Users/x/.daynote/notebook.daynote",
            ["nested"] = new JsonObject { ["a"] = 1, ["b"] = true, ["c"] = (string?)null },
        };
        var before = root.ToJsonString();

        LogRedactor.Redact(root, Denied);

        Assert.Equal(before, root.ToJsonString());
    }

    [Fact]
    public void Replaces_a_denied_key_whose_value_is_an_object_wholesale()
    {
        // When the value is itself an object, the marker replaces the whole subtree — the redactor
        // must not recurse into it and leave inner fields exposed.
        var root = Redacted(new JsonObject
        {
            ["password"] = new JsonObject { ["inner"] = "still-secret", ["hint"] = "secret too" },
        });

        Assert.Equal(LogRedactor.Marker, (string?)root["password"]);
    }

    [Fact]
    public void Recurses_through_arrays_nested_within_arrays()
    {
        var root = Redacted(new JsonObject
        {
            ["rows"] = new JsonArray(
                new JsonArray(new JsonObject { ["token"] = "deep", ["id"] = 7 })),
        });

        var cell = root["rows"]!.AsArray()[0]!.AsArray()[0]!;
        Assert.Equal(LogRedactor.Marker, (string?)cell["token"]);
        Assert.Equal(7, (int)cell["id"]!);
    }

    [Fact]
    public void Does_not_throw_on_scalars_or_null()
    {
        LogRedactor.Redact(null, Denied);
        LogRedactor.Redact(JsonValue.Create(42), Denied);
        LogRedactor.Redact(JsonValue.Create("token"), Denied); // a bare string value is never a field name
    }
}
