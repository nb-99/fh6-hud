using System.Net;
using System.Net.Sockets;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Tests;

public class UdpTelemetryListenerTests : IDisposable
{
    private readonly UdpTelemetryListener _listener = new(0);

    public void Dispose() => _listener.Dispose();

    [Fact]
    public async Task ValidPacket_RaisesEventAndIncrementsCounter()
    {
        var tcs = new TaskCompletionSource<Fh6Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
        _listener.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        var bytes = new Fh6PacketBuilder()
            .IsRaceOn(1)
            .TimestampMs(777u)
            .SpeedMs(27.78f)
            .PowerWatts(200_000f)
            .Build();
        await client.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, _listener.Port));

        var packet = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, packet.IsRaceOn);
        Assert.Equal(777u, packet.TimestampMs);
        Assert.Equal(27.78f, packet.SpeedMs);
        Assert.Equal(1, _listener.PacketsReceived);
    }

    [Fact]
    public async Task SledLengthDatagram_IsIgnored()
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        await client.SendAsync(new byte[232], 232, new IPEndPoint(IPAddress.Loopback, _listener.Port));

        await Task.Delay(150);
        Assert.Equal(0, _listener.PacketsReceived);
    }

    [Fact]
    public async Task InvalidDatagram_DoesNotBreakListener()
    {
        var tcs = new TaskCompletionSource<Fh6Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
        _listener.PacketReceived += (_, packet) => tcs.TrySetResult(packet);

        using var client = new UdpClient(AddressFamily.InterNetwork);
        var endpoint = new IPEndPoint(IPAddress.Loopback, _listener.Port);

        // A 232-byte (Sled-format) datagram first — must be silently dropped.
        await client.SendAsync(new byte[232], 232, endpoint);

        var bytes = new Fh6PacketBuilder().IsRaceOn(1).Build();
        await client.SendAsync(bytes, bytes.Length, endpoint);

        var packet = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, packet.IsRaceOn);
        Assert.Equal(1, _listener.PacketsReceived);
    }
}
