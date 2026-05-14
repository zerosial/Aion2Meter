using System.Collections.Generic;

namespace A2Meter.Dps.Protocol;

/// Aion 2 server-id → server-name table. Ported verbatim from the original.
/// Used by the party parser both as a name source and as a server-id validator
/// (a server id is "valid" iff this table knows it).
internal static class ServerMap
{
    public static readonly Dictionary<int, string> Servers = new()
    {
        // Elyos (1001-1021)
        [1001] = "시엘",     [1002] = "네자칸",   [1003] = "바이젤",
        [1004] = "카이시넬", [1005] = "유스티엘", [1006] = "아리엘",
        [1007] = "프레기온", [1008] = "메스람타에다", [1009] = "히타니에",
        [1010] = "나니아",   [1011] = "타하바타", [1012] = "루터스",
        [1013] = "페르노스", [1014] = "다미누",   [1015] = "카사카",
        [1016] = "바카르마", [1017] = "챈가룽",   [1018] = "코치룽",
        [1019] = "이슈타르", [1020] = "티아마트", [1021] = "포에타",

        // Asmodian (2001-2021)
        [2001] = "이스라펠", [2002] = "지켈",     [2003] = "트리니엘",
        [2004] = "루미엘",   [2005] = "마르쿠탄", [2006] = "아스펠",
        [2007] = "에레슈키갈", [2008] = "브리트라", [2009] = "네몬",
        [2010] = "하달",     [2011] = "루드라",   [2012] = "울고른",
        [2013] = "무닌",     [2014] = "오다르",   [2015] = "젠카카",
        [2016] = "크로메데", [2017] = "콰이링",   [2018] = "바바룽",
        [2019] = "파프니르", [2020] = "인드나흐", [2021] = "이스할겐",
    };

    /// Abbreviated server names for compact display (2 chars, with exceptions).
    public static readonly Dictionary<int, string> ShortNames = BuildShortNames();

    private static Dictionary<int, string> BuildShortNames()
    {
        var overrides = new Dictionary<string, string>
        {
            ["이스할겐"] = "할겐",
        };

        var result = new Dictionary<int, string>();
        foreach (var (id, name) in Servers)
        {
            result[id] = overrides.TryGetValue(name, out var s) ? s
                       : name.Length > 2 ? name[..2]
                       : name;
        }
        return result;
    }

    public static string GetName(int id) =>
        Servers.TryGetValue(id, out var name) ? name : "";

    public static string GetShortName(int id) =>
        ShortNames.TryGetValue(id, out var name) ? name : GetName(id);
}
