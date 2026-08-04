using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Fh6Hud.Telemetry;

namespace Fh6Hud.Simulator;

/// <summary>
/// Sends synthetic FH6 "Data Out" packets to the HUD so it can be developed
/// and tested without the game running.
/// </summary>
public static class Program
{
    private const float MaxRpm = 7000f;
    private const float MaxPowerWatts = 320_000f;

    public static async Task<int> Main(string[] args)
    {
        int port = 45000;
        double rate = 60;
        double seconds = 30;
        var scenario = "launch";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
                case "--rate" when i + 1 < args.Length: rate = double.Parse(args[++i]); break;
                case "--seconds" when i + 1 < args.Length: seconds = double.Parse(args[++i]); break;
                case "--scenario" when i + 1 < args.Length: scenario = args[++i]; break;
                case "--help":
                    Console.WriteLine("Usage: Fh6Hud.Simulator [--port 45000] [--rate 60] [--seconds 30] [--scenario cruise|launch]");
                    return 0;
            }
        }

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var target = new IPEndPoint(IPAddress.Loopback, port);
        var stopwatch = Stopwatch.StartNew();
        long sent = 0;
        long lastCount = 0;
        var nextTick = TimeSpan.Zero;
        var interval = TimeSpan.FromSeconds(1.0 / rate);

        Console.WriteLine($"Simulating FH6 telemetry -> 127.0.0.1:{port} at {rate:0.##} Hz for {seconds:0.#}s (scenario: {scenario})");

        while (stopwatch.Elapsed.TotalSeconds < seconds)
        {
            if (stopwatch.Elapsed < nextTick)
            {
                await Task.Delay(1);
                continue;
            }

            nextTick += interval;
            var packet = BuildPacket(stopwatch.Elapsed.TotalSeconds, scenario);
            socket.SendTo(packet, target);
            sent++;

            if (sent - lastCount >= rate * 2)
            {
                lastCount = sent;
                Console.WriteLine($"  {sent} packets sent ({sent / stopwatch.Elapsed.TotalSeconds:0.#}/s)");
            }
        }

        Console.WriteLine($"Done. {sent} packets sent.");
        return 0;
    }

    private static byte[] BuildPacket(double t, string scenario)
    {
        float speed;
        float fl;
        float fr;
        float rl;
        float rr;

        switch (scenario)
        {
            case "cruise":
                speed = 22f + (float)Math.Sin(t / 5) * 3f;
                float warmup = Math.Clamp((float)t / 25f, 0f, 1f);
                fl = 60f + 50f * warmup + (float)Math.Sin(t / 3) * 2f;
                fr = 60f + 50f * warmup + (float)Math.Cos(t / 3) * 2f;
                rl = 58f + 48f * warmup + (float)Math.Sin(t / 4) * 2f;
                rr = 58f + 48f * warmup + (float)Math.Cos(t / 4) * 2f;
                break;

            case "launch":
            default:
                double cycle = t % 10.0;
                speed = cycle switch
                {
                    < 0.5 => 0f,
                    < 7.5 => (float)((cycle - 0.5) / 7.0 * 90f),
                    < 8.5 => 90f,
                    _ => (float)(90f * (1 - (cycle - 8.5) / 1.5)),
                };
                float launcherTemp = Math.Clamp((float)(t % 12.0) / 10f, 0f, 1f);
                fl = 70f + 45f * launcherTemp;
                fr = fl + 2f;
                rl = fl - 2f;
                rr = fl + 1f;
                break;
        }

        float rpm = speed < 0.1f ? 900f : 900f + speed / 90f * (MaxRpm - 900f);
        float power = MaxPowerWatts * Math.Clamp((rpm - 900f) / 4500f, 0f, 1f);

        var builder = new Fh6PacketBuilder()
            .IsRaceOn(1)
            .TimestampMs((uint)(t * 1000) % 1_000_000u)
            .EngineMaxRpm(MaxRpm)
            .CurrentEngineRpm(rpm)
            .SpeedMs(speed)
            .PowerWatts(power)
            .TorqueNm(450f)
            .TireTempC(fl, fr, rl, rr)
            .Gear((byte)(speed < 0.1f ? 1 : Math.Min(6, (int)(speed / 15) + 1)));

        return builder.Build();
    }
}
