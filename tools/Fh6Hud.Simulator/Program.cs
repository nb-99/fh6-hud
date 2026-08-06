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
    private const float IdleRpm = 900f;
    private const float MaxPowerWatts = 320_000f;

    // The simulated "game" upshifts here; the HUD's shift advisor learns the
    // power curve and typically lands a bit below this, so the SHIFT indicator
    // flashes briefly in every gear.
    private const float SimShiftRpm = 6900f;

    // Road speed (m/s) at redline per gear — i.e. real-feeling gear ratios.
    private static readonly float[] GearTopSpeedMs = { 22.5f, 34.8f, 47.8f, 61.2f, 76.5f, 90f };

    private static int GearForSpeed(float speed)
    {
        for (int g = 0; g < GearTopSpeedMs.Length; g++)
        {
            if (speed <= GearTopSpeedMs[g] * (SimShiftRpm / MaxRpm))
            {
                return g + 1;
            }
        }

        return GearTopSpeedMs.Length;
    }

    private static float RpmFor(float speed, int gear)
    {
        if (speed < 0.1f)
        {
            return IdleRpm;
        }

        float topSpeed = GearTopSpeedMs[Math.Clamp(gear - 1, 0, GearTopSpeedMs.Length - 1)];
        return Math.Max(IdleRpm, speed / topSpeed * MaxRpm);
    }

    // Power ramps to a 320 kW peak at 5600 RPM, then falls off 18% toward
    // redline — the falloff is what creates a pre-redline optimal shift point.
    private static float PowerW(float rpm) =>
        rpm <= 5600f
            ? 150_000f + (rpm - 1000f) * ((MaxPowerWatts - 150_000f) / 4600f)
            : MaxPowerWatts * (1f - 0.18f * (rpm - 5600f) / 1400f);

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

        int gear = GearForSpeed(speed);
        float rpm = RpmFor(speed, gear);
        float power = PowerW(rpm);

        var builder = new Fh6PacketBuilder()
            .IsRaceOn(1)
            .TimestampMs((uint)(t * 1000) % 1_000_000u)
            .EngineMaxRpm(MaxRpm)
            .CurrentEngineRpm(rpm)
            .SpeedMs(speed)
            .PowerWatts(power)
            .TorqueNm(450f)
            .TireTempC(fl, fr, rl, rr)
            .Accel(255)
            .Gear((byte)gear);

        return builder.Build();
    }
}
