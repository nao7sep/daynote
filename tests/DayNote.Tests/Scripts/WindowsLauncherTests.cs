using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace DayNote.Tests.Scripts;

public sealed class WindowsLauncherTests
{
    [WindowsOnlyFact]
    public async Task Runtime_selection_close_readiness_and_publication_use_the_mocked_Windows_contract()
    {
        var root = Path.Combine(Path.GetTempPath(), "daynote-windows-launcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scripts = SourceDirectory();
        var repository = Directory.GetParent(scripts)!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(scripts, "windows-launcher-contract.ps1"), "-RepoDir", repository, "-ScratchDir", root })
            start.ArgumentList.Add(argument);
        try
        {
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            bound.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await child.WaitForExitAsync(bound.Token);
                Assert.True(child.ExitCode == 0, await output + Environment.NewLine + await error);
                Assert.Contains("Windows launcher mocked contracts passed.", await output, StringComparison.Ordinal);
            }
            finally
            {
                if (!child.HasExited)
                {
                    // This is the test's own script interpreter; the fixture only handles fake app processes.
                    child.Kill();
                    await child.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;
}
