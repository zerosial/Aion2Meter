using System.Collections.Generic;

namespace A2Meter.Calc;

public class CombatScoreResult
{
	public int Score { get; set; }

	public int CombatPower { get; set; }

	public string ClassName { get; set; } = "";

	public Dictionary<string, int> SkillLevels { get; set; } = new Dictionary<string, int>();

	public bool HasJonggul { get; set; }

	public bool HasNaked { get; set; }
}
