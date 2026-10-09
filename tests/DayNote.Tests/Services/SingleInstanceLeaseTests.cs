using System;
using System.IO;
using System.Threading;
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
}
