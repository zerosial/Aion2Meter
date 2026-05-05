using System.Collections.Generic;
using System.Text.Json;

namespace A2Meter.Api;

public static class JsonExtensions
{
	public static JsonElement GetProp(this JsonElement el, string name)
	{
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var value))
		{
			return default(JsonElement);
		}
		return value;
	}

	public static string? GetString(this JsonElement el, string name)
	{
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var value))
		{
			return null;
		}
		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.GetRawText(),
			_ => null,
		};
	}

	public static int GetInt(this JsonElement el, string name)
	{
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var value))
		{
			return 0;
		}
		int result;
		return value.ValueKind switch
		{
			JsonValueKind.Number => value.GetInt32(),
			JsonValueKind.String => int.TryParse(value.GetString(), out result) ? result : 0,
			_ => 0,
		};
	}

	public static List<JsonElement> GetPropArray(this JsonElement el, string obj, string arr)
	{
		List<JsonElement> list = new List<JsonElement>();
		JsonElement prop = el.GetProp(obj);
		if (prop.ValueKind != JsonValueKind.Object)
		{
			return list;
		}
		if (!prop.TryGetProperty(arr, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			list.Add(item);
		}
		return list;
	}
}
