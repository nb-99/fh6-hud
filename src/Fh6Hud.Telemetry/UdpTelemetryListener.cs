using System.Net;
using System.Net.Sockets;

namespace Fh6Hud.Telemetry;

/// <summary>
/// Listens for FH6 "Data Out" UDP datagrams on a background thread and
/// publishes parsed packets. Events are raised on the listener thread — never
/// touch UI from handlers directly.
/// </summary>
public sealed class UdpTelemetryListener : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Socket _socket;
    private readonly int _port;
    private readonly TaskCompletionSource _receiveLoopReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _packetsReceived;
    private long _parseFailures;
    private long _receiveErrors;
    private bool _disposed;

    public UdpTelemetryListener(int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // Bounded receive: on macOS/Unix disposing a socket does not wake a
        // blocked ReceiveFrom, so an unbounded receive would deadlock Dispose
        // (and hang the process/runner). With a timeout the loop wakes
        // regularly and exits promptly once cancelled.
        _socket.ReceiveTimeout = 250;
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        _loop = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    public event EventHandler<Fh6Packet>? PacketReceived;

    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);

    /// <summary>Datagrams rejected for an invalid size or failed parsing.</summary>
    public long ParseFailures => Interlocked.Read(ref _parseFailures);

    public long ReceiveErrors => Interlocked.Read(ref _receiveErrors);

    public int Port => _port;

    internal Task ReceiveLoopReady => _receiveLoopReady.Task;

    private void ReceiveLoop(CancellationToken token)
    {
        var buffer = new byte[Fh6Packet.PacketSize + 64];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (!token.IsCancellationRequested)
        {
            try
            {
                _receiveLoopReady.TrySetResult();
                int received = _socket.ReceiveFrom(buffer, SocketFlags.None, ref remote);
                if (received != Fh6Packet.PacketSize)
                {
                    Interlocked.Increment(ref _parseFailures);
                    continue;
                }

                var packet = Fh6Packet.Parse(buffer.AsSpan(0, received));
                if (packet is null)
                {
                    Interlocked.Increment(ref _parseFailures);
                    continue;
                }

                Interlocked.Increment(ref _packetsReceived);
                PacketReceived?.Invoke(this, packet);
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock)
            {
                // Idle wake from ReceiveTimeout — re-check the token.
            }
            catch (SocketException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception)
            {
                Interlocked.Increment(ref _receiveErrors);
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        // Wait for the loop to leave its receive before disposing the socket:
        // on macOS disposing while a receive is in flight blocks forever.
        if (_loop.Wait(TimeSpan.FromSeconds(2)))
        {
            DisposeResources();
            return;
        }

        // If the loop did not stop in time, defer cleanup until it has exited
        // rather than disposing the socket while ReceiveFrom may still run.
        _ = _loop.ContinueWith(
            _ => DisposeResources(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void DisposeResources()
    {
        _socket.Dispose();
        _cts.Dispose();
    }
}
