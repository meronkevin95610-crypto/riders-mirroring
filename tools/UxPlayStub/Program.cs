// UxPlayStub — a tiny shim that imitates just enough of the UxPlay
// (https://github.com/FDH2/UxPlay) stdout protocol to drive
// UxPlayLogParser + AirPlayReceiverProcess in tests and on machines where
// the real GStreamer-based binary cannot be installed.
//
// Why this exists:
//   The real UxPlay is GPLv3 + C++ + GStreamer. Compiling it on Windows
//   needs MSYS2 + the GStreamer SDK which is overkill for what the app
//   needs. Riders Mirroring only consumes UxPlay's stdout — it never
//   links the library or embeds its source — so a process that emits the
//   same log lines is functionally equivalent for our pipeline.
//
// Wire protocol we emit (matched by UxPlayLogParser):
//   "UxPlayStub 1.0 starting"
//   "Server initialized, listening on UDP port <port>"
//   For each simulated device every ~10s:
//     "[1234/567890.123:INFO:CONSOLE(1)] \"VIDEO has been received from \\\"<name>\\\"\""
//   On Ctrl+C / "quit":
//     "disconnected <name>"

using System.Globalization;
using System.Text;
using System.Threading;

namespace UxPlayStub;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += OnCancel;

        var port = ParsePort(args);
        var serverName = ParseServerName(args);
        var deviceName = ParseDeviceName(args);

        Emit("UxPlayStub 1.0 starting up");
        Thread.Sleep(150);
        Emit("Riders Mirroring shim — not the real UxPlay");
        Thread.Sleep(150);
        Emit($"Server initialized, listening on UDP port {port}");
        Thread.Sleep(150);
        Emit($"Bonjour announced as '{serverName}'");
        Thread.Sleep(150);
        Emit("Waiting for connections...");

        var tick = 0;
        var connected = false;
        var exitCode = 0;

        // Loop until the parent process kills us. We emit a "device
        // connected" event shortly after start, then a "disconnected"
        // event some seconds later so the UI can demonstrate the full
        // lifecycle without an actual iPhone in the room.
        while (!_stopRequested)
        {
            Thread.Sleep(1000);
            tick++;

            if (!connected && tick >= 3)
            {
                EmitChromiumStyle($"VIDEO has been received from \"{deviceName}\"");
                connected = true;
            }
            else if (connected && tick >= 15)
            {
                EmitChromiumStyle($"RTSP client disconnected from {deviceName}");
                connected = false;
            }
            else if (!connected && tick >= 22)
            {
                // Re-cycle so a human watching the UI sees motion.
                EmitChromiumStyle($"VIDEO has been received from \"{deviceName}\"");
                connected = true;
                tick = 5;
            }
        }

        Emit("Stopping AirPlay receiver...");
        return exitCode;
    }

    private static bool _stopRequested;

    private static void OnCancel(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _stopRequested = true;
    }

    private static void Emit(string line)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }

    private static void EmitChromiumStyle(string message)
    {
        // Match UxPlay 1.72+ Chromium-shaped log lines so the existing
        // UxPlayLogParser regex picks them up without modification.
        var stamp = DateTimeOffset.UtcNow.ToString("HHmmssfff", CultureInfo.InvariantCulture);
        Console.Out.WriteLine($"[{stamp}]:INFO:CONSOLE(1)] \"{message.Replace("\"", "\\\"")}\"");
        Console.Out.Flush();
    }

    private static int ParsePort(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-p" || args[i] == "--port")
            {
                if (int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                {
                    return p;
                }
            }
        }
        return 7000;
    }

    private static string ParseServerName(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-n" || args[i] == "--name")
            {
                return args[i + 1];
            }
        }
        return "Riders Test";
    }

    private static string ParseDeviceName(string[] args)
    {
        // Look for "-d <name>" (device label, UxPlay exposes this) or
        // fall back to a representative iPhone name.
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-d" || args[i] == "--device")
            {
                return args[i + 1];
            }
        }
        return "iPhone de démo";
    }
}
