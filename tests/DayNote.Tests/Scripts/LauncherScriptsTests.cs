using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace DayNote.Tests.Scripts;

public sealed class LauncherScriptsTests
{
    [MacOnlyFact]
    public async Task Exact_application_selection_and_cooperative_close_respect_refusal_and_pending_saves()
    {
        var root = NewRoot();
        try
        {
            var script = Path.Combine(root, "contract.js");
            File.WriteAllText(script, File.ReadAllText(Path.Combine(Scripts(), "launcher-apps.js")) + "\n" + """
                function run() {
                    function check(value, message) { if (!value) throw new Error(message); }
                    var app = { path: '/repo/publish/DayNote', exited: false };
                    var tool = { path: '/usr/bin/dotnet', command: 'build /repo/publish/DayNote', exited: false };
                    var other = { path: '/other/DayNote', exited: false };
                    var time = 0, calls = 0, accept = true, finish = false;
                    var adapter = {
                        path: function(a) { return a.path; },
                        exited: function(a) { return a.exited; },
                        close: function(a) { calls++; check(a === app, 'Selected an unrelated process'); return accept; },
                        now: function() { return time; },
                        pump: function() { time++; if (finish) app.exited = true; }
                    };
                    var apps = [tool, other, app], paths = [app.path];
                    check(matchingApplications(apps, paths, adapter).length === 1, 'Not exact identity');
                    accept = false;
                    try { closeApplications(apps, paths, adapter, 5); throw new Error('missed refusal'); }
                    catch (error) { check(error.message.indexOf('ordinary quit') >= 0, 'Refusal was not retained'); }
                    check(calls === 1 && !app.exited, 'Refusal changed the app');
                    accept = true;
                    try { closeApplications(apps, paths, adapter, 5); throw new Error('missed pending save'); }
                    catch (error) { check(error.message.indexOf('still open') >= 0, 'Pending save was not retained'); }
                    check(!app.exited && !other.exited && !tool.exited, 'Forced a process exit');
                    finish = true;
                    closeApplications(apps, paths, adapter, 5);
                    check(app.exited && !other.exited && !tool.exited, 'Wrong close outcome');
                    check(matchingApplications(apps, paths, adapter).length === 0, 'Exited app still ready');
                    return 'passed';
                }
                """);
            var result = await Run("/usr/bin/osascript", ["-l", "JavaScript", script]);
            Assert.True(result.Code == 0, result.Error);
            Assert.Equal("passed", result.Output.Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    [MacOnlyFact]
    public async Task Launchers_abort_on_cancel_or_failed_build_and_publish_only_a_complete_candidate()
    {
        foreach (var scenario in new[] { ("run-built.command", true, false), ("rebuild.command", true, false),
            ("rebuild.command", false, true), ("rebuild.command", false, false) })
        {
            var root = NewRoot();
            try
            {
                var scripts = Directory.CreateDirectory(Path.Combine(root, "scripts")).FullName;
                Directory.CreateDirectory(Path.Combine(root, ".git"));
                foreach (var name in new[] { scenario.Item1, "launcher-runtime.sh", "launcher-apps.js" })
                    File.Copy(Path.Combine(Scripts(), name), Path.Combine(scripts, name));
                var executable = Path.Combine(root, "publish", "DayNote.app", "Contents", "MacOS", "DayNote");
                Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, "old");
                if (OperatingSystem.IsMacOS())
                    File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var bin = Directory.CreateDirectory(Path.Combine(root, "tools")).FullName;
                WriteTool(bin, "osascript", """
                    echo "application:$4" >> "$TRACE"
                    if [ "$4" = close ] && [ "$MOCK_CLOSE_FAIL" = 1 ]; then exit 1; fi
                    if [ "$4" = ready ]; then echo ready; else echo closed; fi
                    """);
                WriteTool(bin, "open", "echo opened >> \"$TRACE\"");
                WriteTool(bin, "uname", "echo arm64");
                WriteTool(bin, "dotnet", """
                    echo build >> "$TRACE"
                    if [ "$MOCK_BUILD_FAIL" = 1 ]; then exit 1; fi
                    while [ "$#" -gt 0 ]; do
                      if [ "$1" = -o ]; then shift; destination="$1"; break; fi
                      shift
                    done
                    mkdir -p "$destination/DayNote.app/Contents/MacOS"
                    echo new > "$destination/DayNote.app/Contents/MacOS/DayNote"
                    chmod +x "$destination/DayNote.app/Contents/MacOS/DayNote"
                    """);
                var trace = Path.Combine(root, "trace");
                var result = await Run("/bin/bash", [Path.Combine(scripts, scenario.Item1)], info =>
                {
                    info.Environment["PATH"] = bin + ":" + Environment.GetEnvironmentVariable("PATH");
                    info.Environment["TRACE"] = trace;
                    info.Environment["MOCK_CLOSE_FAIL"] = scenario.Item2 ? "1" : "0";
                    info.Environment["MOCK_BUILD_FAIL"] = scenario.Item3 ? "1" : "0";
                });
                var success = !scenario.Item2 && !scenario.Item3;
                Assert.Equal(success, result.Code == 0);
                Assert.Equal(success ? "new" : "old", File.ReadAllText(executable).Trim());
                var events = File.Exists(trace) ? File.ReadAllLines(trace) : [];
                Assert.Equal(success, events.Contains("opened"));
                if (scenario.Item3) Assert.DoesNotContain("application:close", events);
                Assert.Empty(Directory.GetDirectories(root, "publish-candidate-*"));
                Assert.Empty(Directory.GetDirectories(root, "publish-previous-*"));
            }
            finally { Directory.Delete(root, true); }
        }
    }

    private static void WriteTool(string directory, string name, string body)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "#!/bin/bash\nset -eu\n" + body + "\n");
        if (OperatingSystem.IsMacOS())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static async Task<(int Code, string Output, string Error)> Run(string command, string[] arguments,
        Action<ProcessStartInfo>? configure = null)
    {
        var info = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        configure?.Invoke(info);
        using var process = Process.Start(info)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
        }
    }

    private static string NewRoot() => Directory.CreateTempSubdirectory("daynote-launcher-").FullName;
    private static string Scripts([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "..", "scripts"));
}
