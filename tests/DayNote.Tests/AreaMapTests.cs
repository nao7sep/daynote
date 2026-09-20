using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace DayNote.Tests;

/// <summary>
/// <c>tests/README.md</c> is the balance judgement the tests-folder-conventions require: it names
/// DayNote's areas and the tests standing for each, so a reader can tell what a green
/// <c>dotnet test</c> covered. A map is only worth that if it cannot rot silently, so these hold it
/// to what is on disk — a renamed or deleted test file breaks the run instead of quietly hollowing
/// out the map, and an area left without a representative is a failure rather than an omission.
/// </summary>
public sealed class AreaMapTests
{
    [Fact]
    public void The_Area_Map_Lists_More_Than_One_Area()
    {
        var areas = Areas();
        Assert.True(
            areas.Count > 1,
            $"{MapPath()}: lists {areas.Count} area(s); a map with one area is not a balance judgement.");
    }

    [Fact]
    public void Every_Area_Names_At_Least_One_Test()
    {
        var unrepresented = Areas().Where(area => area.Paths.Count == 0).Select(area => area.Name).ToList();
        Assert.True(
            unrepresented.Count == 0,
            $"{MapPath()}: these areas name no test standing for them: {string.Join("; ", unrepresented)}.");
    }

    [Fact]
    public void Every_Test_Path_The_Area_Map_Names_Exists()
    {
        var testsRoot = TestsRoot();
        var missing = Areas()
            .SelectMany(area => area.Paths, (area, path) => (area.Name, Path: path))
            .Where(listed => !File.Exists(
                Path.Combine(testsRoot, listed.Path.Replace('/', Path.DirectorySeparatorChar))))
            .Select(listed => $"{listed.Name}: {listed.Path}")
            .ToList();
        Assert.True(
            missing.Count == 0,
            $"{MapPath()}: these listed test paths do not exist: {string.Join("; ", missing)}.");
    }

    /// <summary>Reads the map's Markdown table: the lines starting with a pipe, less the header row
    /// and the row of dashes, each split on its pipes into the three cells between the outer ones.</summary>
    private static List<(string Name, List<string> Paths)> Areas()
    {
        var mapPath = MapPath();
        var rows = File.ReadAllLines(mapPath)
            .Where(line => line.StartsWith('|'))
            .Skip(2)
            .ToList();

        Assert.True(rows.Count > 0, $"{mapPath}: found no area rows under the table header.");

        var areas = new List<(string, List<string>)>();
        foreach (var row in rows)
        {
            // A row is pipe-delimited and pipe-terminated, so the split's first and last entries are
            // the empty strings outside the outer pipes; three cells must remain between them.
            var cells = row.Split('|')[1..^1];
            Assert.True(
                row.EndsWith('|') && cells.Length == 3,
                $"{mapPath}: expected a three-column row ending in a pipe, got: {row}");

            areas.Add((
                cells[0].Trim(),
                Regex.Matches(cells[2], "`([^`]+)`").Select(match => match.Groups[1].Value).ToList()));
        }

        return areas;
    }

    private static string MapPath() => Path.Combine(TestsRoot(), "README.md");

    private static string TestsRoot([CallerFilePath] string callerPath = "") =>
        // This file: <repo>/tests/DayNote.Tests/AreaMapTests.cs
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, ".."));
}
