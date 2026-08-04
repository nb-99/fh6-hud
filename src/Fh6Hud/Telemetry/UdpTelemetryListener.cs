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

    public UdpTelemetryListener(int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        _port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
        _loop = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    public event EventHandler<Fh6Packet>? PacketReceived;

    public long PacketsReceived { get; private set; }

    public long ReceiveErrors { get; private set; }

    public int Port => _port;

    private void ReceiveLoop(CancellationToken token)
    {
        var buffer = new byte[Fh6Packet.PacketSize + 64];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (!token.IsCancellationRequested)
        {
            try
            {
                int received = _socket.ReceiveFrom(buffer, SocketFlags.None, ref remote);
                if (received != Fh6Packet.PacketSize)
                {
                    continue;
                }

                var packet = Fh6Packet.Parse(buffer.AsSpan(0, received));
                if (packet is null)
                {
                    continue;
                }

                PacketsReceived++;
                PacketReceived?.Invoke(this, packet);
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
                ReceiveErrors++;
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket.Dispose();
        _cts.Dispose();
    }
}
