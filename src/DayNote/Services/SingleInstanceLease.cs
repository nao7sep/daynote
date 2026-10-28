using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DayNote.Services;

/// <summary>
/// Owns one DayNote storage root and routes a second launch to the owner's window.
/// This prevents independent autosave loops from replacing the same binder snapshot.
/// </summary>
internal sealed class SingleInstanceLease : IDisposable
{
    private const string LeaseFileName = "instance.lock";
    private const string EndpointFileName = "instance.endpoint";
    private static readonly TimeSpan NotifyTimeout = TimeSpan.FromSeconds(2);
    private static SingleInstanceLease? _current;

    private FileStream? _ownership;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private Thread? _listenerThread;
    private Action? _activate;

    private SingleInstanceLease(FileStream ownership, TcpListener listener)
    {
        _ownership = ownership;
        _listener = listener;
    }

    /// <summary>
    /// Claims <paramref name="root"/> for this process, or, when another DayNote holds it, asks that one to
    /// bring its window forward and returns false. The claim is held until <see cref="Dispose(TimeSpan)"/>.
    /// </summary>
    public static bool TryAcquire(string root, out SingleInstanceLease? lease,
        Func<FileStream, int>? lockUnix = null)
    {
        var endpointPath = Path.Combine(root, EndpointFileName);
        FileStream ownership;
        try
        {
            // Every alias reaches the same stable file. Never unlink it: another opener must keep
            // contending for this inode rather than creating a second independent reservation.
            ownership = new FileStream(Path.Combine(root, LeaseFileName), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error) when (IsSharingConflict(error))
        {
            NotifyPrimary(endpointPath);
            lease = null;
            return false;
        }

        TcpListener? listener = null;
        try
        {
            // Unix FileStream sharing is best effort and can be disabled. Verify the actual flock
            // so unsupported locking cannot silently admit competing shared-store writers.
            var lockingError = OperatingSystem.IsWindows() ? 0 : (lockUnix ?? LockUnix)(ownership);
            if (lockingError != 0)
            {
                if (IsWouldBlock(lockingError))
                {
                    ownership.Dispose();
                    NotifyPrimary(endpointPath);
                    lease = null;
                    return false;
                }
                throw new IOException("Could not reserve the storage folder.", new Win32Exception(lockingError));
            }
            // Listening from here on: a second launch that connects before the window registers its
            // handler waits in the listener's backlog until then.
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            PublishEndpoint(endpointPath, port);

            lease = new SingleInstanceLease(ownership, listener);
            _current = lease;
            return true;
        }
        catch
        {
            listener?.Stop();
            ownership.Dispose();
            throw;
        }
    }

    private static bool IsSharingConflict(IOException error) => OperatingSystem.IsWindows()
        ? error.HResult == unchecked((int)0x80070020) : IsWouldBlock(error.HResult);

    private static bool IsWouldBlock(int error) => error == (OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD() ? 35 : 11);

    private static int LockUnix(FileStream ownership)
    {
        while (Flock(ownership.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != 4) // An interrupted syscall has no settled locking outcome; retry it.
                return error;
        }
        return 0;
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);

    /// <summary>
    /// Gives the claim its window's activation and starts accepting second launches, once; activations that
    /// arrived earlier are waiting for it.
    /// </summary>
    public static void RegisterOwnerActivationHandler(Action handler)
    {
        if (_current is not { } current || current._listenerThread is not null)
            return;
        current._activate = handler;
        current._listenerThread = new Thread(current.ListenForActivation)
        {
            IsBackground = true,
            Name = "DayNote activation",
        };
        current._listenerThread.Start();
    }

    private static void PublishEndpoint(string path, int port)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        var bytes = Encoding.ASCII.GetBytes(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }

    private static void NotifyPrimary(string endpointPath)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < NotifyTimeout)
        {
            try
            {
                var text = File.ReadAllText(endpointPath).Trim();
                if (int.TryParse(text, out var port) && port is > 0 and <= 65535)
                {
                    using var client = new TcpClient();
                    client.Connect(IPAddress.Loopback, port);
                    var request = Encoding.ASCII.GetBytes("activate");
                    client.GetStream().Write(request);
                    return;
                }
            }
            catch (IOException)
            {
                // The owner may still be publishing its endpoint; retry briefly.
            }
            catch (SocketException)
            {
                // A stale endpoint may still be visible during owner startup.
            }
            Thread.Sleep(20);
        }
    }

    private void ListenForActivation()
    {
        var stopping = _stopping.Token;
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var client = _listener.AcceptTcpClient();
                using var stream = client.GetStream();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                // One frame is exactly the eight ASCII bytes "activate". Do not wait for EOF;
                // any bytes after the frame are ignored when this connection closes.
                var request = new byte[8];
                // An incomplete IValueTaskSource-backed ValueTask cannot be waited synchronously
                // through GetResult directly; Task supplies the blocking wait on this owner thread.
                stream.ReadExactlyAsync(request, deadline.Token).AsTask().GetAwaiter().GetResult();
                if (request.AsSpan().SequenceEqual("activate"u8))
                    _activate!();
            }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException
                or IOException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // A partial request, deadline, or loopback failure ends only this connection.
                if (stopping.IsCancellationRequested)
                    return;
            }
        }
    }

    public void Dispose() => Dispose(Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Releases the claim on the thread that took it. The listener stops at once, and a second launch it is
    /// still reading is waited for within <paramref name="remaining"/>, what is left of the exit's deadline;
    /// the listener's own one-second read deadline bounds that wait too.
    /// </summary>
    public void Dispose(TimeSpan remaining)
    {
        if (_ownership is not { } ownership)
            return;
        _ownership = null;
        if (ReferenceEquals(_current, this))
            _current = null;

        try
        {
            _stopping.Cancel();
            _listener.Stop();
            _listenerThread?.Join(remaining);
        }
        finally
        {
            ownership.Dispose();
        }
    }
}
