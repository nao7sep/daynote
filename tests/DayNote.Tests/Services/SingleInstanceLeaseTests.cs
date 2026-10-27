using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DayNote.Services;
using Xunit;

namespace DayNote.Tests.Services;

public sealed class SingleInstanceLeaseTests
{
    [Fact]
    public void SecondClaimActivatesOwnerAndCannotOpenSameRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "daynote-instance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var activated = new ManualResetEventSlim();
        Exception? ownerFailure = null;
        var ownerThread = new Thread(() =>
        {
            try
            {
                Assert.True(SingleInstanceLease.TryAcquire(root, out var owner));
                using (owner)
                {
                    acquired.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception ex)
            {
                ownerFailure = ex;
                acquired.Set();
            }
        });
        try
        {
            ownerThread.Start();
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Null(ownerFailure);

            SingleInstanceLease.RegisterOwnerActivationHandler(activated.Set);
            Assert.False(SingleInstanceLease.TryAcquire(root, out var duplicate));
            Assert.Null(duplicate);
            Assert.True(activated.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

            release.Set();
            Assert.True(ownerThread.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(ownerFailure);
            Assert.True(SingleInstanceLease.TryAcquire(root, out var successor));
            successor?.Dispose();
        }
        finally
        {
            release.Set();
            ownerThread.Join(TimeSpan.FromSeconds(10));
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Incomplete_activation_does_not_block_the_next_client()
    {
        var root = NewRoot();
        using var activated = new ManualResetEventSlim();
        try
        {
            await WithOwnerAsync(root, async () =>
            {
                SingleInstanceLease.RegisterOwnerActivationHandler(activated.Set);
                using var incomplete = await ConnectActivationClientAsync(root);
                await incomplete.GetStream().WriteAsync("act"u8.ToArray(), TestContext.Current.CancellationToken);
                using var valid = await ConnectActivationClientAsync(root);
                await valid.GetStream().WriteAsync("activate"u8.ToArray(), TestContext.Current.CancellationToken);

                Assert.True(activated.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Complete_activation_frame_activates_without_peer_eof()
    {
        var root = NewRoot();
        using var activated = new ManualResetEventSlim();
        try
        {
            await WithOwnerAsync(root, async () =>
            {
                SingleInstanceLease.RegisterOwnerActivationHandler(activated.Set);
                using var client = await ConnectActivationClientAsync(root);
                await client.GetStream().WriteAsync("activate"u8.ToArray(), TestContext.Current.CancellationToken);

                // The peer remains open while the owner handles the fixed-length frame.
                Assert.True(activated.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static int ActivationPort(string root) => int.Parse(
        File.ReadAllText(Path.Combine(root, "instance.endpoint")), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<TcpClient> ConnectActivationClientAsync(string root)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, ActivationPort(root), TestContext.Current.CancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    [WindowsOnlyFact]
    public void RootsDifferingOnlyInLetterCaseAreOneRootOnWindows()
    {
        // Windows paths ignore case, so two spellings of one storage root are the same folder and must share
        // one lease: a second DayNote on the other spelling would write the same files.
        var root = Path.Combine(Path.GetTempPath(), "DayNote-Instance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? ownerFailure = null;
        var ownerThread = new Thread(() =>
        {
            try
            {
                Assert.True(SingleInstanceLease.TryAcquire(root, out var owner));
                using (owner)
                {
                    acquired.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception ex)
            {
                ownerFailure = ex;
                acquired.Set();
            }
        });
        try
        {
            ownerThread.Start();
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Null(ownerFailure);

            Assert.False(SingleInstanceLease.TryAcquire(root.ToLowerInvariant(), out var other));
            Assert.Null(other);
        }
        finally
        {
            release.Set();
            ownerThread.Join(TimeSpan.FromSeconds(10));
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ActivationRouterRetainsAnEarlyRequest()
    {
        var router = new ActivationRequestRouter();
        var activations = 0;

        router.Request();
        router.Register(() => activations++);
        router.Request();

        Assert.Equal(2, activations);
    }

    [Fact]
    public async Task Previous_build_ownership_excludes_a_new_process_until_release()
    {
        var root = NewRoot();
        try
        {
            await WithOwnerAsync(root, async () =>
                Assert.Contains("LEASE-REJECTED", await ClaimInChildAsync(root), StringComparison.Ordinal), legacy: true);
            Assert.Contains("LEASE-CLAIMED", await ClaimInChildAsync(root), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Independent_roots_and_cross_process_handoff_keep_separate_owners()
    {
        var root = NewRoot();
        var otherRoot = NewRoot();
        try
        {
            await WithOwnerAsync(root, async () =>
            {
                Assert.Contains("LEASE-REJECTED", await ClaimInChildAsync(root), StringComparison.Ordinal);
                Assert.Contains("LEASE-CLAIMED", await ClaimInChildAsync(otherRoot), StringComparison.Ordinal);
            });
            Assert.Contains("LEASE-CLAIMED", await ClaimInChildAsync(root), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(otherRoot, recursive: true);
        }
    }

    [MacOnlyFact]
    public async Task Directory_aliases_exclude_a_process_even_when_managed_file_locking_is_disabled()
    {
        var parent = NewRoot();
        var root = Path.Combine(parent, "owned");
        var alias = Path.Combine(parent, "alias");
        Directory.CreateDirectory(root);
        Directory.CreateSymbolicLink(alias, root);
        try
        {
            await WithOwnerAsync(root, async () =>
                Assert.Contains("LEASE-REJECTED", await ClaimInChildAsync(alias, disableManagedLocking: true), StringComparison.Ordinal));
            Assert.Contains("LEASE-CLAIMED", await ClaimInChildAsync(alias, disableManagedLocking: true), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(alias);
            Directory.Delete(parent, recursive: true);
        }
    }

    [MacOnlyFact]
    public async Task Unsupported_native_locking_fails_without_leaving_an_owner()
    {
        var root = NewRoot();
        try
        {
            Assert.Throws<IOException>(() => SingleInstanceLease.TryAcquire(root, out _, lockUnix: _ => 45));
            Assert.Contains("LEASE-CLAIMED", await ClaimInChildAsync(root), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Bounded_disposal_returns_while_owned_close_work_is_pending()
    {
        // Isolate a deliberately held physical close: even a broken unbounded Dispose cannot hang
        // the ordinary suite or leave its disposable root owned by a surviving child.
        var root = NewRoot();
        try
        {
            Assert.Contains("LEASE-CLOSE-SETTLED", await ClaimInChildAsync(root, mode: "--lease-close-harness"), StringComparison.Ordinal);
            Assert.Contains("LEASE-CLAIMED", await ClaimInChildAsync(root), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static void RunBoundedCloseProof(string root)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Assert.True(SingleInstanceLease.TryAcquire(root, out var lease, beforeFileClose: () =>
        {
            entered.Set();
            release.Wait();
        }));
        try
        {
            lease!.Dispose(TimeSpan.Zero);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(lease.Closing.IsCompleted);
            Assert.Throws<IOException>(() =>
            {
                using var conflicting = new FileStream(Path.Combine(root, "instance.lock"), FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None);
            });
            // A different thread is refused while the callback holds before file Dispose begins.
            // This does not simulate a stalled native close: Unix may unlock before close returns.
            // Same-thread named mutex acquisition would be recursive rather than contested.
            Exception? contenderFailure = null;
            var contender = new Thread(() =>
            {
                try
                {
                    var claimed = SingleInstanceLease.TryAcquire(root, out var duplicate);
                    duplicate?.Dispose();
                    Assert.False(claimed);
                }
                catch (Exception error) { contenderFailure = error; }
            });
            contender.Start();
            Assert.True(contender.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(contenderFailure);
        }
        finally
        {
            release.Set();
            Assert.True(lease!.Closing.Wait(TimeSpan.FromSeconds(10)));
            lease.Dispose(); // The original acquiring thread releases the compatibility mutex.
        }
        Assert.True(File.Exists(Path.Combine(root, "instance.lock")));
        Assert.True(SingleInstanceLease.TryAcquire(root, out var successor));
        successor?.Dispose();
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "daynote-lease-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task WithOwnerAsync(string root, Func<Task> check, bool legacy = false)
    {
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? failure = null;
        var owner = new Thread(() =>
        {
            try
            {
                if (legacy)
                {
                    // This is the immutable previous-build protocol, deliberately independent of
                    // the new implementation so dropping compatibility fails this regression.
                    var path = Path.GetFullPath(root);
                    if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
                    using var mutex = new Mutex(false, "DayNote-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))));
                    Assert.True(mutex.WaitOne(TimeSpan.Zero));
                    try { acquired.Set(); release.Wait(); }
                    finally { mutex.ReleaseMutex(); }
                }
                else
                {
                    Assert.True(SingleInstanceLease.TryAcquire(root, out var lease));
                    using (lease) { acquired.Set(); release.Wait(); }
                }
            }
            catch (Exception error) { failure = error; acquired.Set(); }
        });
        owner.Start();
        try
        {
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Null(failure);
            await check();
        }
        finally
        {
            release.Set();
            Assert.True(owner.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(failure);
        }
    }

    private static async Task<string> ClaimInChildAsync(string root, bool disableManagedLocking = false,
        string mode = "--lease-claim-harness")
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(SingleInstanceLeaseTests).Assembly.Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(root);
        start.Environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = disableManagedLocking ? "1" : "0";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(bound.Token);
            Assert.True(process.ExitCode == 0, await error);
            return await output;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    [MacOnlyFact]
    public void Existing_directory_aliases_reserve_one_storage_owner()
    {
        var parent = Path.Combine(Path.GetTempPath(), "daynote-alias-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "OwnedRoot");
        var alias = Path.Combine(parent, "linked-root");
        Directory.CreateDirectory(root);
        Directory.CreateSymbolicLink(alias, root);
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Exception? ownerFailure = null;
        var ownerThread = new Thread(() =>
        {
            try
            {
                Assert.True(SingleInstanceLease.TryAcquire(root, out var owner));
                using (owner)
                {
                    acquired.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception ex) { ownerFailure = ex; acquired.Set(); }
        });
        try
        {
            ownerThread.Start();
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Null(ownerFailure);
            AssertRejected(alias);
            var caseAlias = Path.Combine(parent, "ownedroot");
            if (Directory.Exists(caseAlias))
                AssertRejected(caseAlias);
        }
        finally
        {
            release.Set();
            ownerThread.Join(TimeSpan.FromSeconds(10));
            Directory.Delete(alias);
            Directory.Delete(parent, recursive: true);
        }

        static void AssertRejected(string path)
        {
            var claimed = SingleInstanceLease.TryAcquire(path, out var duplicate);
            duplicate?.Dispose();
            Assert.False(claimed);
            Assert.Null(duplicate);
        }
    }

}
