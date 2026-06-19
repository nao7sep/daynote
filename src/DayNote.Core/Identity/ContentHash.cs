using System.Security.Cryptography;
using System.Text;

namespace DayNote.Core.Identity;

/// <summary>
/// SHA-256 content hashing, used to detect external modification of notebook files. Ported from
/// quickdeck's content-hash comparison.
/// </summary>
public static class ContentHash
{
    /// <summary>Returns the lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="content"/>.</summary>
    public static string Sha256Hex(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Returns the lowercase hex SHA-256 of a file's bytes, streamed so a large attachment is not
    /// loaded fully into memory. Used to deduplicate attachments by content.
    /// </summary>
    public static string Sha256HexFile(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}
