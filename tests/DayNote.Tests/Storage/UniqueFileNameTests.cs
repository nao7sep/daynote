using DayNote.Core.Storage;
using Xunit;

namespace DayNote.Tests.Storage;

/// <summary>
/// Guards the case-insensitive uniqueness of assigned attachment filenames (storage-path-conventions):
/// a candidate that differs from an existing name only in case must be disambiguated, not returned
/// as-is, or it would silently clobber the sibling on macOS/Windows. The human filename is preserved
/// verbatim otherwise — never lowercased.
/// </summary>
public sealed class UniqueFileNameTests
{
    [Fact]
    public void Pick_returns_the_name_unchanged_when_the_directory_is_empty()
    {
        Assert.Equal("Photo.png", UniqueFileName.Pick([], "Photo.png"));
    }

    [Fact]
    public void Pick_returns_the_name_unchanged_when_no_existing_name_matches()
    {
        Assert.Equal("Photo.png", UniqueFileName.Pick(["other.txt", "image.jpg"], "Photo.png"));
    }

    [Fact]
    public void Pick_disambiguates_an_exact_collision()
    {
        Assert.Equal("Photo (1).png", UniqueFileName.Pick(["Photo.png"], "Photo.png"));
    }

    [Fact]
    public void Pick_disambiguates_a_name_that_differs_only_in_case()
    {
        Assert.Equal("photo (1).png", UniqueFileName.Pick(["Photo.png"], "photo.png"));
    }

    [Fact]
    public void Pick_preserves_the_candidate_casing_and_does_not_lowercase_it()
    {
        Assert.Equal("MixedCase.PNG", UniqueFileName.Pick(["other.png"], "MixedCase.PNG"));
    }

    [Fact]
    public void Pick_skips_case_insensitive_matches_until_a_free_suffix_is_found()
    {
        var result = UniqueFileName.Pick(["photo.png", "PHOTO (1).PNG"], "Photo.png");
        Assert.Equal("Photo (2).png", result);
    }
}
