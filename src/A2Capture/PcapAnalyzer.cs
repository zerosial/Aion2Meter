// PcapAnalyzer — scans a pcap through the protocol pipeline and dumps messages
// containing specific Korean nicknames.
//
// Build:   dotnet build E:\A2Viewer\A2Meter\src\PcapAnalyzer\PcapAnalyzer.csproj
// Run:     dotnet run --project E:\A2Viewer\A2Meter\src\PcapAnalyzer\PcapAnalyzer.csproj
//
// This file lives in A2Capture for reference but is compiled via the PcapAnalyzer project.
//
// Replays capture-003455.pcapng through TcpReassembler -> StreamProcessor,
// then searches each dispatched message for UTF-8 byte sequences of:
//   부경  (EB B6 80 EA B2 BD)
//   유다희 (EC 9C A0 EB 8B A4 ED 9D AC)
//   띯   (EB 9D AF)

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using A2Meter.Dps.Protocol;

var pcapPath = @"E:\A2Viewer\A2Meter\src\A2Capture\bin\Debug\net8.0\captures\20260509-003454\capture-003455.pcapng";

if (!File.Exists(pcapPath))
{
    Console.Error.WriteLine($"File not found: {pcapPath}");
    return 1;
}

// UTF-8 byte patterns for the Korean nicknames.
var targets = new (string Name, byte[] Utf8)[]
{
    ("부경",   new byte[] { 0xEB, 0xB6, 0x80, 0xEA, 0xB2, 0xBD }),
    ("유다희", new byte[] { 0xEC, 0x9C, 0xA0, 0xEB, 0x8B, 0xA4, 0xED, 0x9D, 0xAC }),
    ("띯",    new byte[] { 0xEB, 0x9D, 0xAF }),
};

Console.WriteLine($"[analyzer] pcap: {pcapPath}");
Console.WriteLine($"[analyzer] searching for: {string.Join(", ", targets.Select(t => t.Name))}");
Console.WriteLine();

int serverPort = 13328;
int totalMessages = 0;
int matchCount = 0;
var matches = new List<(int MsgIndex, string[] Names, byte[] Data)>();

// Hook: StreamProcessor dispatches each framed message here.
void OnMessage(byte[] data, int offset, int length)
{
    totalMessages++;
    var span = new ReadOnlySpan<byte>(data, offset, length);
    var found = new List<string>();

    foreach (var (name, pattern) in targets)
    {
        if (ContainsPattern(span, pattern))
            found.Add(name);
    }

    if (found.Count > 0)
    {
        matchCount++;
        // Copy message bytes so we can dump them later (buffer may be reused).
        var copy = new byte[length];
        Buffer.BlockCopy(data, offset, copy, 0, length);
        matches.Add((totalMessages, found.ToArray(), copy));

        Console.WriteLine($"=== MATCH #{matchCount} (msg #{totalMessages}, len={length}) names=[{string.Join(", ", found)}] ===");
        DumpHexAscii(copy, 0, copy.Length);
        Console.WriteLine();
    }
}

var stream = new StreamProcessor(OnMessage, msg => { /* suppress stream diag */ });
var flows = new Dictionary<(IPAddress, int, IPAddress, int), TcpReassembler>();

// Read pcap and feed server->client segments through the pipeline.
using var dev = new CaptureFileReaderDevice(pcapPath);
dev.Open();
long totalPackets = 0;

while (dev.GetNextPacket(out var pc) == GetPacketStatus.PacketRead)
{
    totalPackets++;
    var rc = pc.GetPacket();
    try
    {
        var pkt = rc.GetPacket();
        var ip  = pkt.Extract<IPPacket>();
        var tcp = pkt.Extract<TcpPacket>();
        if (ip is null || tcp is null) continue;
        var payload = tcp.PayloadData ?? Array.Empty<byte>();
        if (payload.Length == 0) continue;
        if (tcp.SourcePort != serverPort) continue; // only server->client

        var key = (ip.SourceAddress, tcp.SourcePort, ip.DestinationAddress, tcp.DestinationPort);
        if (!flows.TryGetValue(key, out var rasm))
        {
            rasm = new TcpReassembler(b => stream.ProcessData(b));
            flows[key] = rasm;
        }
        rasm.Feed(tcp.SequenceNumber, payload);
    }
    catch { }
}

Console.WriteLine($"[analyzer] pcap packets: {totalPackets}");
Console.WriteLine($"[analyzer] dispatched messages: {totalMessages}");
Console.WriteLine($"[analyzer] messages with target names: {matchCount}");
Console.WriteLine();

// Summary of matches.
if (matches.Count > 0)
{
    Console.WriteLine("=== MATCH SUMMARY ===");
    foreach (var (idx, names, data) in matches)
    {
        Console.WriteLine($"  msg #{idx}: len={data.Length} names=[{string.Join(", ", names)}]");
        // Show the first few bytes to identify the opcode/tag.
        Console.Write("    header: ");
        for (int i = 0; i < Math.Min(20, data.Length); i++)
            Console.Write($"{data[i]:X2} ");
        Console.WriteLine();

        // For each name found, show its position and surrounding context.
        foreach (var name in names)
        {
            var pattern = targets.First(t => t.Name == name).Utf8;
            int pos = IndexOfPattern(data, pattern);
            if (pos >= 0)
            {
                Console.WriteLine($"    '{name}' at offset {pos}:");
                int contextStart = Math.Max(0, pos - 16);
                int contextEnd = Math.Min(data.Length, pos + pattern.Length + 32);
                Console.Write("      ");
                for (int i = contextStart; i < contextEnd; i++)
                {
                    if (i == pos) Console.Write("[");
                    Console.Write($"{data[i]:X2}");
                    if (i == pos + pattern.Length - 1) Console.Write("]");
                    Console.Write(" ");
                }
                Console.WriteLine();

                // Also try to decode surrounding UTF-8 strings.
                TryDecodeStringsAround(data, pos, pattern.Length);
            }
        }
    }
}

// Also do a raw scan: search every server->client TCP payload for the
// names WITHOUT going through the protocol pipeline, to catch any that
// the StreamProcessor might miss due to framing issues.
Console.WriteLine();
Console.WriteLine("=== RAW TCP PAYLOAD SCAN (no protocol parsing) ===");
dev.Close();
using var dev2 = new CaptureFileReaderDevice(pcapPath);
dev2.Open();
int rawMatchCount = 0;
while (dev2.GetNextPacket(out var pc2) == GetPacketStatus.PacketRead)
{
    var rc2 = pc2.GetPacket();
    try
    {
        var pkt = rc2.GetPacket();
        var ip  = pkt.Extract<IPPacket>();
        var tcp = pkt.Extract<TcpPacket>();
        if (ip is null || tcp is null) continue;
        var payload = tcp.PayloadData ?? Array.Empty<byte>();
        if (payload.Length == 0) continue;

        foreach (var (name, pattern) in targets)
        {
            int pos = IndexOfPattern(payload, pattern);
            if (pos >= 0)
            {
                rawMatchCount++;
                Console.WriteLine($"  RAW: '{name}' in {ip.SourceAddress}:{tcp.SourcePort}->{ip.DestinationAddress}:{tcp.DestinationPort} seq={tcp.SequenceNumber} payloadLen={payload.Length} at offset {pos}");
                int from = Math.Max(0, pos - 8);
                int to = Math.Min(payload.Length, pos + pattern.Length + 16);
                Console.Write("    ");
                for (int i = from; i < to; i++)
                {
                    if (i == pos) Console.Write("[");
                    Console.Write($"{payload[i]:X2}");
                    if (i == pos + pattern.Length - 1) Console.Write("]");
                    Console.Write(" ");
                }
                Console.WriteLine();
            }
        }
    }
    catch { }
}
Console.WriteLine($"  raw matches: {rawMatchCount}");

Console.WriteLine();
Console.WriteLine("[analyzer] done.");
return 0;

// ── helpers ──────────────────────────────────────────────────────────────

static bool ContainsPattern(ReadOnlySpan<byte> data, byte[] pattern)
{
    if (data.Length < pattern.Length) return false;
    return data.IndexOf(pattern) >= 0;
}

static int IndexOfPattern(byte[] data, byte[] pattern)
{
    return new ReadOnlySpan<byte>(data).IndexOf(pattern);
}

static void DumpHexAscii(byte[] data, int offset, int length)
{
    int end = offset + length;
    for (int row = offset; row < end; row += 16)
    {
        Console.Write($"  {row - offset:X4}: ");
        // Hex part.
        for (int col = 0; col < 16; col++)
        {
            int i = row + col;
            if (i < end)
                Console.Write($"{data[i]:X2} ");
            else
                Console.Write("   ");
            if (col == 7) Console.Write(" ");
        }
        Console.Write(" |");
        // ASCII part.
        for (int col = 0; col < 16; col++)
        {
            int i = row + col;
            if (i < end)
            {
                byte b = data[i];
                Console.Write(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }
        }
        Console.WriteLine("|");
    }
}

static void TryDecodeStringsAround(byte[] data, int namePos, int nameLen)
{
    // Look backwards for a length prefix (1 or 2 bytes before the name).
    // Common patterns: varint length prefix, or a 1-byte length.
    if (namePos >= 1)
    {
        int prefixByte = data[namePos - 1];
        if (prefixByte == nameLen)
        {
            Console.WriteLine($"      -> length prefix at offset {namePos - 1}: 0x{prefixByte:X2} = {prefixByte} (matches name length)");
        }
    }

    // Try to decode UTF-8 text around the name to find surrounding fields.
    int scanFrom = Math.Max(0, namePos - 40);
    int scanTo = Math.Min(data.Length, namePos + nameLen + 60);
    var region = new byte[scanTo - scanFrom];
    Buffer.BlockCopy(data, scanFrom, region, 0, region.Length);
    try
    {
        var text = Encoding.UTF8.GetString(region);
        var printable = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || (c >= '가' && c <= '힣') || c == '[' || c == ']')
                printable.Append(c);
            else
                printable.Append('.');
        }
        Console.WriteLine($"      -> UTF-8 region: {printable}");
    }
    catch { }
}
