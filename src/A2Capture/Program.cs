// a2cap — packet capture tool for A2Meter regression testing.
//
// Records TCP packets from the selected adapter into rotating .pcapng files
// and writes a JSON sidecar manifest per session. The pcap files can be
// replayed offline (PacketSniffer.Replay — TODO) so DpsMeter can be tested
// without re-running combats in-game.
//
// Usage:
//   a2cap --list
//   a2cap [--adapter N|name] [--filter "tcp"] [--out DIR]
//         [--rotate-mb 200] [--rotate-min 30] [--quiet]
//
// Stop with Ctrl-C. Manifest is finalized on clean shutdown.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace A2Capture;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var opts = CliOptions.Parse(args);

            if (opts.ShowHelp) { PrintHelp(); return 0; }
            if (opts.ListOnly) return ListAdapters();

            EnsurePcapAvailable();

            var device = SelectAdapter(opts.AdapterSpec)
                         ?? throw new InvalidOperationException("No suitable adapter found.");

            var session = new CaptureSession(device, opts);
            session.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[a2cap] " + ex.Message);
            return 1;
        }
    }

    private static void PrintHelp() => Console.WriteLine("""
        a2cap — A2Meter packet capture

        Usage:
          a2cap --list                              List available adapters and exit
          a2cap [options]                           Capture until Ctrl-C

        Options:
          --adapter <index|name|nicname>            Adapter to use (default: first up adapter)
          --port <N>                                TCP port to capture (default: 13328 — Aion 2 server seed port).
                                                    Use --port 0 to capture all TCP.
          --port-range LO-HI                        Capture a port range (only when --port 0). Default: 1024-65535
                                                    if --port 0 and no range is given, captures all TCP.
          --filter "<bpf>"                          Custom BPF filter — overrides --port / --port-range.
          --out     <dir>                           Output directory (default: ./captures)
          --rotate-mb  <int>                        Rotate pcap when file exceeds N MB (default: 200)
          --rotate-min <int>                        Rotate pcap every N minutes (default: 30)
          --quiet                                   Suppress live counters
          --help, -h                                Show this help

        Notes:
          * The Aion 2 client connects to the game server on TCP/13328 by default.
            A2Power's sniffer treats this port as a fast-path (1 hit auto-confirm).
          * If a future build moves to a different port, run with --port 0 (or
            --port-range 1024-65535) to capture all candidates, then narrow once
            you know the real port.
        """);

    private static int ListAdapters()
    {
        EnsurePcapAvailable();
        Console.WriteLine($"libpcap version: {Pcap.SharpPcapVersion}");
        Console.WriteLine();
        Console.WriteLine($"{ "#",-3} {"Status",-6} {"Friendly Name",-40} {"Description"}");
        int i = 0;
        foreach (var d in CaptureDeviceList.Instance)
        {
            var status = (d is LibPcapLiveDevice live && live.Interface.Addresses?.Count > 0) ? "up" : "?";
            var friendly = (d is LibPcapLiveDevice ld) ? (ld.Interface.FriendlyName ?? "") : "";
            Console.WriteLine($"{i++,-3} {status,-6} {Truncate(friendly, 40),-40} {d.Description}");
        }
        return 0;
    }

    private static void EnsurePcapAvailable()
    {
        try
        {
            _ = CaptureDeviceList.Instance.Count;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Npcap is not installed (or not loadable). Install Npcap from https://npcap.com — " +
                "tick \"Install Npcap in WinPcap API-compatible Mode\". " +
                "Original error: " + ex.Message, ex);
        }
    }

    private static ICaptureDevice? SelectAdapter(string? spec)
    {
        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0) return null;

        if (string.IsNullOrWhiteSpace(spec))
        {
            // Prefer a connected adapter with at least one IPv4 address.
            foreach (var d in devices)
                if (d is LibPcapLiveDevice ld && HasIPv4(ld)) return d;
            return devices[0];
        }

        if (int.TryParse(spec, out int idx) && idx >= 0 && idx < devices.Count)
            return devices[idx];

        foreach (var d in devices)
        {
            if (d.Description?.Contains(spec, StringComparison.OrdinalIgnoreCase) == true) return d;
            if (d is LibPcapLiveDevice ld &&
                (ld.Interface.FriendlyName?.Equals(spec, StringComparison.OrdinalIgnoreCase) == true ||
                 ld.Interface.Name?.Contains(spec, StringComparison.OrdinalIgnoreCase) == true))
                return d;
        }
        return null;
    }

    private static bool HasIPv4(LibPcapLiveDevice d)
    {
        if (d.Interface.Addresses is null) return false;
        foreach (var a in d.Interface.Addresses)
            if (a.Addr?.ipAddress?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return true;
        return false;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

internal sealed class CliOptions
{
    /// Aion 2 game server's known seed port. The original A2Power sniffer
    /// auto-confirms the server port after a single packet on this port;
    /// other ports require multiple magic-payload hits to confirm.
    public const int DefaultServerPort = 13328;

    public bool ListOnly;
    public bool ShowHelp;
    public string? AdapterSpec;
    public int Port = DefaultServerPort;
    public string? PortRange;       // e.g. "1024-65535" — only used when Port == 0
    public string? CustomFilter;    // overrides everything when set
    public string OutDir = "captures";
    public int RotateMb = 200;
    public int RotateMin = 30;
    public bool Quiet;

    /// Resolves the BPF filter actually applied to the capture device.
    public string ResolveFilter()
    {
        if (!string.IsNullOrWhiteSpace(CustomFilter)) return CustomFilter!;
        if (Port > 0) return $"tcp port {Port}";
        if (!string.IsNullOrWhiteSpace(PortRange))
        {
            var (lo, hi) = ParseRange(PortRange!);
            return $"tcp portrange {lo}-{hi}";
        }
        return "tcp";
    }

    private static (int Lo, int Hi) ParseRange(string s)
    {
        var parts = s.Split('-', 2);
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--list":       o.ListOnly = true; break;
                case "--help": case "-h": o.ShowHelp = true; break;
                case "--adapter":    o.AdapterSpec = args[++i]; break;
                case "--port":       o.Port = int.Parse(args[++i]); break;
                case "--port-range": o.PortRange = args[++i]; break;
                case "--filter":     o.CustomFilter = args[++i]; break;
                case "--out":        o.OutDir = args[++i]; break;
                case "--rotate-mb":  o.RotateMb = int.Parse(args[++i]); break;
                case "--rotate-min": o.RotateMin = int.Parse(args[++i]); break;
                case "--quiet":      o.Quiet = true; break;
                default: throw new ArgumentException("Unknown option: " + args[i]);
            }
        }
        return o;
    }
}

/// One capture session: opens the adapter, writes rotating pcap files,
/// updates a sidecar JSON manifest, and prints live counters.
internal sealed class CaptureSession
{
    private readonly ICaptureDevice _device;
    private readonly CliOptions _opts;
    private readonly string _sessionId;
    private readonly string _sessionDir;
    private readonly string _manifestPath;
    private readonly Manifest _manifest;
    private readonly object _writerLock = new();

    private CaptureFileWriterDevice? _writer;
    private string? _currentFile;
    private DateTime _currentFileStart;
    private long _currentFileBytes;

    private long _totalPackets;
    private long _totalBytes;
    private long _droppedDevice;
    private long _droppedInterface;

    public CaptureSession(ICaptureDevice device, CliOptions opts)
    {
        _device = device;
        _opts = opts;
        _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        _sessionDir = Path.Combine(opts.OutDir, _sessionId);
        Directory.CreateDirectory(_sessionDir);
        _manifestPath = Path.Combine(_sessionDir, "manifest.json");

        _manifest = new Manifest
        {
            SessionId = _sessionId,
            StartedAt = DateTime.UtcNow,
            Adapter = new AdapterInfo
            {
                Name = device.Name,
                Description = device.Description,
                FriendlyName = (device is LibPcapLiveDevice ld) ? ld.Interface.FriendlyName : null,
                Mac = (device is LibPcapLiveDevice m) ? m.Interface.MacAddress?.ToString() : null,
            },
            Filter = opts.ResolveFilter(),
            Port = opts.Port,
            PortRange = opts.PortRange,
            RotateMb = opts.RotateMb,
            RotateMin = opts.RotateMin,
            Host = new HostInfo
            {
                Machine = Environment.MachineName,
                Os = Environment.OSVersion.ToString(),
                LibpcapVersion = Pcap.SharpPcapVersion.ToString(),
            },
            Files = new List<string>(),
        };
    }

    public void Run()
    {
        _device.Open(DeviceModes.Promiscuous, read_timeout: 1000);
        var filter = _opts.ResolveFilter();
        if (!string.IsNullOrWhiteSpace(filter))
            _device.Filter = filter;

        OpenNewFile();
        _device.OnPacketArrival += OnPacket;

        var stopRequested = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopRequested = true;
        };

        Console.WriteLine($"[a2cap] {_device.Description}");
        Console.WriteLine($"[a2cap] filter='{filter}'  out={_sessionDir}");
        Console.WriteLine("[a2cap] press Ctrl-C to stop");
        WriteManifest();

        _device.StartCapture();

        using var ticker = new Timer(_ => Tick(), null, 1000, 1000);
        while (!stopRequested) Thread.Sleep(100);

        Console.WriteLine();
        Console.WriteLine("[a2cap] stopping...");
        try { _device.StopCapture(); } catch { }
        try { _device.Close(); }       catch { }
        lock (_writerLock) { _writer?.Close(); _writer = null; }

        _manifest.EndedAt = DateTime.UtcNow;
        _manifest.PacketsTotal = _totalPackets;
        _manifest.BytesTotal = _totalBytes;
        WriteManifest();
        Console.WriteLine($"[a2cap] done. {_totalPackets:n0} pkts / {Mb(_totalBytes):n1} MB across {_manifest.Files.Count} file(s).");
    }

    private void OnPacket(object sender, PacketCapture e)
    {
        var raw = e.GetPacket();
        Interlocked.Increment(ref _totalPackets);
        Interlocked.Add(ref _totalBytes, raw.Data.Length);

        lock (_writerLock)
        {
            _writer?.Write(raw);
            _currentFileBytes += raw.Data.Length;

            bool overSize = _currentFileBytes >= (long)_opts.RotateMb * 1024 * 1024;
            bool overTime = (DateTime.UtcNow - _currentFileStart).TotalMinutes >= _opts.RotateMin;
            if (overSize || overTime) RotateLocked();
        }
    }

    private void OpenNewFile()
    {
        lock (_writerLock) RotateLocked();
    }

    private void RotateLocked()
    {
        _writer?.Close();
        var name = $"capture-{DateTime.Now:HHmmss}.pcapng";
        _currentFile = Path.Combine(_sessionDir, name);
        _currentFileStart = DateTime.UtcNow;
        _currentFileBytes = 0;
        _writer = new CaptureFileWriterDevice(_currentFile);
        _writer.Open(_device);
        _manifest.Files.Add(name);
        WriteManifest();
    }

    private void Tick()
    {
        try
        {
            var stats = _device.Statistics;
            if (stats != null)
            {
                _droppedDevice = (long)stats.DroppedPackets;
                _droppedInterface = (long)stats.InterfaceDroppedPackets;
            }
        }
        catch { }

        if (_opts.Quiet) return;
        Console.Write($"\r[a2cap] {_totalPackets,12:n0} pkts  {Mb(_totalBytes),9:n1} MB  drop dev={_droppedDevice} if={_droppedInterface}     ");
    }

    private void WriteManifest()
    {
        try
        {
            var json = JsonSerializer.Serialize(_manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });
            File.WriteAllText(_manifestPath, json);
        }
        catch { /* best-effort */ }
    }

    private static double Mb(long bytes) => bytes / (1024.0 * 1024.0);
}

internal sealed class Manifest
{
    public string SessionId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public AdapterInfo Adapter { get; set; } = new();
    public string Filter { get; set; } = "";
    public int Port { get; set; }
    public string? PortRange { get; set; }
    public int RotateMb { get; set; }
    public int RotateMin { get; set; }
    public HostInfo Host { get; set; } = new();
    public long PacketsTotal { get; set; }
    public long BytesTotal { get; set; }
    public List<string> Files { get; set; } = new();
}

internal sealed class AdapterInfo
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? FriendlyName { get; set; }
    public string? Mac { get; set; }
}

internal sealed class HostInfo
{
    public string? Machine { get; set; }
    public string? Os { get; set; }
    public string? LibpcapVersion { get; set; }
}
