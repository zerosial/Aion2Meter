using System.Collections.Generic;

namespace PacketEngine;

/// Game job-code to Korean job-name mapping.
/// Matches A2Viewer.Dps.JobMapping.GameToName.
internal static class JobMapping
{
    private static readonly Dictionary<int, string> GameToName = new()
    {
        [1]  = "검성",
        [2]  = "수호성",
        [3]  = "살성",
        [4]  = "궁성",
        [5]  = "정령성",
        [6]  = "호법성",
        [7]  = "마도성",
        [8]  = "치유성",
        [9]  = "호법성",
        [10] = "총성",
        [11] = "기갑성",
        [12] = "음유성",
        [13] = "투사",
        [14] = "천마",
        [15] = "자객",
        [16] = "마궁",
        [17] = "주술사",
        [18] = "범사",
        [19] = "흑마법사",
        [20] = "치유사",
        [21] = "범사",
        [22] = "총사",
        [23] = "기갑사",
        [24] = "음유사",
    };

    public static string GetName(int jobCode)
        => GameToName.TryGetValue(jobCode, out var name) ? name : "직업불명";
}
