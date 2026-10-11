using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace DayNote.Services;

internal sealed class ActivationRequestRouter
{
    private readonly object _gate = new();
    private Action? _handler;
    private bool _pending;

    public void Register(Action handler)
    {
        var invoke = false;
        lock (_gate)
        {
            _handler = handler;
            invoke = _pending;
            _pending = false;
        }
        if (invoke)
            handler();
    }

    public void Request()
    {
        Action? handler;
        lock (_gate)
        {
            handler = _handler;
            if (handler is null)
                _pending = true;
        }
        handler?.Invoke();
    }
}

/// <summary>
/// Owns one DayNote storage root and routes a second launch to the owner's window.
/// This prevents independent autosave loops from replacing the same binder snapshot.
/// </summary>
internal sealed class SingleInstanceLease : IDisposable
{
    private const string LeaseFileName = "instance.lock";
    private const string EndpointFileName = "instance.endpoint";
    private static readonly TimeSpan NotifyTimeout = TimeSpan.FromSeconds(2);
    private static readonly object CurrentGate = new();
    private static SingleInstanceLease? _current;

    private FileStream? _ownership;
    private Mutex? _legacyMutex;
    private readonly Action? _beforeFileClose;
    private Task _closing = Task.CompletedTask;
    internal Task Closing => _closing;
    private readonly TcpListener _listener;
    private readonly Thread _listenerThread;
    private readonly ActivationRequestRouter _activationRouter = new();
    private volatile bool _disposed;

    private SingleInstanceLease(FileStream ownership, Mutex legacyMutex, TcpListener listener, Action? beforeFileClose)
    {
        _ownership = ownership;
        _legacyMutex = legacyMutex;
        _beforeFileClose = beforeFileClose;
        _listener = listener;
        _listenerThread = new Thread(ListenForActivation)
        {
            IsBackground = true,
            Name = "DayNote activation",
        };
        _listenerThread.Start();
    }

    public static bool TryAcquire(string root, out SingleInstanceLease? lease,
        Func<FileStream, int>? lockUnix = null, Action? beforeFileClose = null)
    {
        var endpointPath = Path.Combine(root, EndpointFileName);
        // Retain the previous protocol while an older build can still be running. The stable file
        // below supplies current-build alias correctness; this mutex preserves previous-build
        // exclusion on its original spelling (older Unix builds cannot recognize other aliases).
        var legacyMutex = new Mutex(false, LegacyMutexName(root));
        bool legacyOwned;
        try { legacyOwned = legacyMutex.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException) { legacyOwned = true; }
        if (!legacyOwned)
        {
            legacyMutex.Dispose();
            NotifyPrimary(endpointPath);
            lease = null;
            return false;
        }
        var legacyReleased = false;
        void ReleaseLegacy()
        {
            if (legacyReleased) return;
            legacyReleased = true;
            legacyMutex.ReleaseMutex();
            legacyMutex.Dispose();
        }
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
            ReleaseLegacy();
            NotifyPrimary(endpointPath);
            lease = null;
            return false;
        }
        catch
        {
            ReleaseLegacy();
            throw;
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
                    ReleaseLegacy();
                    NotifyPrimary(endpointPath);
                    lease = null;
                    return false;
                }
                throw new IOException("Could not reserve the storage folder.", new Win32Exception(lockingError));
            }
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            PublishEndpoint(endpointPath, port);

            lease = new SingleInstanceLease(ownership, legacyMutex, listener, beforeFileClose);
            lock (CurrentGate)
                _current = lease;
            return true;
        }
        catch
        {
            listener?.Stop();
            ownership.Dispose();
            ReleaseLegacy();
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

    private static string LegacyMutexName(string root)
    {
        var path = Path.GetFullPath(root);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return "DayNote-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int descriptor, int operation);

    public static void RegisterOwnerActivationHandler(Action handler)
    {
        SingleInstanceLease? current;
        lock (CurrentGate)
            current = _current;
        current?._activationRouter.Register(handler);
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
        while (!_disposed)
        {
            try
            {
                using var client = _listener.AcceptTcpClient();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                if (reader.ReadToEnd().StartsWith("activate", StringComparison.Ordinal))
                    _activationRouter.Request();
            }
            catch (SocketException) when (_disposed)
            {
                return;
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                return;
            }
            catch (IOException)
            {
                // A client can disconnect mid-request; keep the owner available
                // for the next legitimate activation rather than losing the listener.
            }
            catch (SocketException)
            {
                // A transient loopback failure is not a reason to surrender ownership.
            }
        }
    }

    public void Dispose() => Dispose(TimeSpan.FromSeconds(2));

    /// <summary>Shares <paramref name="closeBound"/> between listener exit and physical file close.</summary>
    public void Dispose(TimeSpan closeBound)
    {
        if (_disposed)
        {
            ReleaseLegacyAfterClose();
            return;
        }
        _disposed = true;
        var elapsed = Stopwatch.StartNew();

        lock (CurrentGate)
        {
            if (ReferenceEquals(_current, this))
                _current = null;
        }

        _listener.Stop();
        var ownership = Interlocked.Exchange(ref _ownership, null);
        _closing = Task.Run(() =>
        {
            try { _beforeFileClose?.Invoke(); }
            finally
            {
                ownership?.Dispose();
                GC.KeepAlive(this); // Retain unfinished close work and the compatibility handle after the bound.
            }
        });
        _ = _closing.ContinueWith(task => Debug.WriteLine(task.Exception),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _listenerThread.Join(closeBound);
        var remaining = closeBound - elapsed.Elapsed;
        if (!_closing.IsCompleted && remaining > TimeSpan.Zero)
            ((IAsyncResult)_closing).AsyncWaitHandle.WaitOne(remaining);
        ReleaseLegacyAfterClose();
    }

    private void ReleaseLegacyAfterClose()
    {
        if (!_closing.IsCompleted)
            return;
        var mutex = Interlocked.Exchange(ref _legacyMutex, null);
        if (mutex is null)
            return;
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
