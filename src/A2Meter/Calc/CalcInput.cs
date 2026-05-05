using System.Collections.Generic;

namespace A2Meter.Calc;

public class CalcInput
{
	public string EquipmentJson { get; set; } = "[]";

	public string AccessoriesJson { get; set; } = "[]";

	public string StatDataJson { get; set; } = "{}";

	public string DaevanionDataJson { get; set; } = "null";

	public string TitlesJson { get; set; } = "[]";

	public string WingName { get; set; } = "";

	public string SkillsJson { get; set; } = "[]";

	public string StigmasJson { get; set; } = "[]";

	public string JobName { get; set; } = "";

	public string CharacterDataJson { get; set; } = "{}";

	public Dictionary<string, int> ArcanaSetCounts { get; set; } = new Dictionary<string, int>
	{
		["magic"] = 0,
		["vitality"] = 0,
		["purity"] = 0,
		["frenzy"] = 0
	};
}
