using System.Collections.Generic;
using System.Text.RegularExpressions;
using DayNote.Core.Identity;
using Xunit;

namespace DayNote.Tests.Identity;

/// <summary>
/// Identifiers double as attachment directory names, so they must stay within a 21-character
/// URL-safe alphabet and be unique within a binder (checked case-insensitively for Windows).
/// </summary>
public sealed class IdGeneratorTests
{
    private static readonly Regex UrlSafe = new("^[A-Za-z0-9_-]{21}$");

    [Fact]
    public void New_is_twenty_one_url_safe_characters()
    {
        var id = IdGenerator.New();

        Assert.Equal(21, id.Length);
        Assert.Matches(UrlSafe, id);
    }

    [Fact]
    public void New_produces_distinct_values()
    {
        var ids = new HashSet<string>();
        for (var i = 0; i < 1000; i++)
        {
            ids.Add(IdGenerator.New());
        }

        Assert.Equal(1000, ids.Count);
    }

    [Fact]
    public void New_unique_avoids_a_case_insensitive_collision_with_existing_ids()
    {
        var existing = new[] { "AbCdEfGhIjKlMnOpQrStU" };

        var id = IdGenerator.NewUnique(existing);

        Assert.Matches(UrlSafe, id);
        Assert.DoesNotContain(existing, e => string.Equals(e, id, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void New_unique_works_with_an_empty_existing_set()
    {
        Assert.Matches(UrlSafe, IdGenerator.NewUnique(new string[0]));
    }
}
