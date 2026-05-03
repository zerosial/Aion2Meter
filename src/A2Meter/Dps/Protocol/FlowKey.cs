using System;
using System.Net;

namespace A2Meter.Dps.Protocol;

/// 4-tuple identifying a TCP flow direction. Two flows for one socket pair
/// (client→server and server→client) get separate keys, which is what the
/// reassembler wants.
internal readonly record struct FlowKey(IPAddress Src, int SrcPort, IPAddress Dst, int DstPort)
{
    public override int GetHashCode() => HashCode.Combine(Src, SrcPort, Dst, DstPort);
}
