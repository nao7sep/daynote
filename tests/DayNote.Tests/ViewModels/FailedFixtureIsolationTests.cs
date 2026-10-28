using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DayNote.Tests.ViewModels;

public sealed class FailedFixtureIsolationTests
{
    [Theory]
    [InlineData("earlier-open")]
    [InlineData("published-cleanup")]
    [InlineData("removed-thumbnail")]
    public async Task Failure_unwind_settles_work_before_releasing_the_fixture_root(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "daynote-failed-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(FailedFixtureIsolationTests).Assembly.Location);
        start.ArgumentList.Add("--failed-fixture-harness");
        start.ArgumentList.Add(scenario);
        start.ArgumentList.Add(root);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.True(process.ExitCode == 0, await error);
            Assert.Contains("FAILED-FIXTURE-SETTLED", await output, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
