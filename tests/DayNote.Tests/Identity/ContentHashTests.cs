using System;
using System.IO;
using System.Text;
using DayNote.Core.Identity;
using Xunit;

namespace DayNote.Tests.Identity;

/// <summary>
/// The content hash underpins external-change detection and attachment deduplication, so it must be a
/// stable, lowercase-hex SHA-256 of the bytes — matched here against the canonical NIST vectors so a
/// drift in encoding or casing is caught.
/// </summary>
public sealed class ContentHashTests
{
    [Fact]
    public void Hashes_the_empty_string_to_the_known_sha256_vector()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ContentHash.Sha256Hex(string.Empty));
    }

    [Fact]
    public void Hashes_abc_to_the_known_sha256_vector_in_lowercase_hex()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ContentHash.Sha256Hex("abc"));
    }

    [Fact]
    public void Identical_content_hashes_equal_and_different_content_differs()
    {
        Assert.Equal(ContentHash.Sha256Hex("note body"), ContentHash.Sha256Hex("note body"));
        Assert.NotEqual(ContentHash.Sha256Hex("note body"), ContentHash.Sha256Hex("note body "));
    }

    [Fact]
    public void Hashes_a_file_to_the_same_value_as_its_bytes()
    {
        // The file hash drives attachment dedup; for a UTF-8 file (no BOM) it must equal the string hash.
        var path = Path.Combine(Path.GetTempPath(), "daynote-hash-" + IdGenerator.New());
        try
        {
            File.WriteAllText(path, "abc", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Equal(ContentHash.Sha256Hex("abc"), ContentHash.Sha256HexFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
