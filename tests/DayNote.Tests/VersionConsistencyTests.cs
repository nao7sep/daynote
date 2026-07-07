using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace DayNote.Tests;

/// <summary>
/// The app version has one source of truth — <c>Directory.Build.props</c>' <c>&lt;Version&gt;</c>, per the
/// app-release-conventions — with kept-in-sync copies nothing derives automatically: <c>macOS/Info.plist</c>
/// (<c>CFBundleVersion</c> and <c>CFBundleShortVersionString</c>) and <c>src/DayNote/app.manifest</c> (a
/// four-part <c>assemblyIdentity version</c>, always ending <c>.0</c>). This test guards those copies
/// against drifting from the SSOT on a version bump.
/// </summary>
public sealed class VersionConsistencyTests
{
    [Fact]
    public void Info_Plist_And_App_Manifest_Match_The_Directory_Build_Props_Version()
    {
        var repoRoot = FindRepoRoot();

        var propsPath = Path.Combine(repoRoot, "Directory.Build.props");
        var version = ReadFirstMatch(propsPath, @"<Version>([^<]+)</Version>");

        var plistPath = Path.Combine(repoRoot, "macOS", "Info.plist");
        var plistText = File.ReadAllText(plistPath);
        AssertPlistKeyMatches(plistText, "CFBundleVersion", version, plistPath, propsPath);
        AssertPlistKeyMatches(plistText, "CFBundleShortVersionString", version, plistPath, propsPath);

        var manifestPath = Path.Combine(repoRoot, "src", "DayNote", "app.manifest");
        var manifestVersion = ReadFirstMatch(manifestPath, "assemblyIdentity version=\"([^\"]+)\"");
        var expectedManifestVersion = version + ".0";
        Assert.True(
            manifestVersion == expectedManifestVersion,
            $"{manifestPath}: assemblyIdentity version is '{manifestVersion}' but expected " +
            $"'{expectedManifestVersion}' (derived from {propsPath}'s Version '{version}').");
    }

    private static void AssertPlistKeyMatches(string plistText, string key, string expected, string plistPath, string propsPath)
    {
        var match = Regex.Match(plistText, $@"<key>{Regex.Escape(key)}</key>\s*<string>([^<]+)</string>");
        Assert.True(match.Success, $"{plistPath}: could not find a <key>{key}</key> entry with a <string> value.");

        var actual = match.Groups[1].Value;
        Assert.True(
            actual == expected,
            $"{plistPath}: {key} is '{actual}' but expected '{expected}' (from {propsPath}'s Version).");
    }

    private static string ReadFirstMatch(string path, string pattern)
    {
        var text = File.ReadAllText(path);
        var match = Regex.Match(text, pattern);
        Assert.True(match.Success, $"{path}: no match for /{pattern}/.");
        return match.Groups[1].Value;
    }

    /// <summary>Walks up from the test assembly's directory to find the repo root, identified by
    /// <c>DayNote.slnx</c> — robust regardless of Debug/Release output subfolders.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DayNote.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (DayNote.slnx) above " + AppContext.BaseDirectory);
    }
}
