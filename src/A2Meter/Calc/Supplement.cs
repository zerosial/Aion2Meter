using System;
using System.Collections.Generic;
using System.Text.Json;
using A2Meter.Api;

namespace A2Meter.Calc;

public static class Supplement
{
	private const int TotalIntPets = 41;

	private const int TotalWildPets = 65;

	public static SupplementResult CalcSupplement(JsonElement statData, Dictionary<int, JsonElement> itemDetails)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		if (statData.ValueKind == JsonValueKind.Object && statData.TryGetProperty("statList", out var value))
		{
			foreach (JsonElement item in value.EnumerateArray())
			{
				string text = item.GetString("type") ?? "";
				int num4 = ParseInt(item, "value");
				switch (text)
				{
				case "STR":
					num = num4;
					break;
				case "DEX":
					num2 = num4;
					break;
				case "INT":
					num3 = num4;
					break;
				}
			}
		}
		int str = 0;
		int dex = 0;
		int intStat = 0;
		foreach (JsonElement value2 in itemDetails.Values)
		{
			AccumulateStats(value2, "mainStats", ref str, ref dex, ref intStat, skipExceed: true);
			AccumulateStats(value2, "subStats", ref str, ref dex, ref intStat, skipExceed: false);
		}
		int num5 = Math.Max(num - str, 0);
		int num6 = Math.Max(num2 - dex, 0);
		int num7 = Math.Max(num3 - intStat, 0);
		int num8 = Math.Min(num5, 41);
		int num9 = Math.Min(num7, num8);
		return new SupplementResult
		{
			PurePower = num5,
			PureAgility = num6,
			PureInt = num7,
			IntelligentPetCriticalMin = (num8 - num9) * 2 + num9 * 5,
			IntelligentPetCriticalMax = Math.Max(num8 * 5, 41),
			WildPetAccuracyMin = 0,
			WildPetAccuracyMax = Math.Max(num6 * 5, 65)
		};
	}

	private static void AccumulateStats(JsonElement item, string arrayName, ref int str, ref int dex, ref int intStat, bool skipExceed)
	{
		if (!item.TryGetProperty(arrayName, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item2 in value.EnumerateArray())
		{
			if (!skipExceed || !item2.TryGetProperty("exceed", out var value2) || value2.ValueKind != JsonValueKind.True)
			{
				int num = ParseInt(item2, "value") + ParseInt(item2, "extra");
				string text = item2.GetString("id") ?? "";
				string text2 = item2.GetString("name") ?? "";
				if (text == "STR" || text2 == "위력")
				{
					str += num;
				}
				else if (text == "DEX" || text2 == "민첩")
				{
					dex += num;
				}
				else if (text == "INT" || text2 == "지식")
				{
					intStat += num;
				}
			}
		}
	}

	private static int ParseInt(JsonElement el, string prop)
	{
		if (!el.TryGetProperty(prop, out var value))
		{
			return 0;
		}
		if (value.ValueKind == JsonValueKind.Number)
		{
			return value.GetInt32();
		}
		if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var result))
		{
			return result;
		}
		return 0;
	}
}
