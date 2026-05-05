using System.Text.Json;

namespace A2Meter.Calc;

public class CalcResult
{
	public JsonElement AttackPower { get; set; }

	public JsonElement CriticalHit { get; set; }

	public JsonElement DamageAmplification { get; set; }

	public JsonElement CombatSpeed { get; set; }

	public JsonElement CooldownReduction { get; set; }

	public JsonElement StunHit { get; set; }

	public JsonElement MultiHit { get; set; }

	public JsonElement Perfect { get; set; }

	public JsonElement Accuracy { get; set; }

	public JsonElement SkillDamage { get; set; }

	public double WeaponMinAttack { get; set; }

	public double WeaponMaxAttack { get; set; }

	public bool IsAttackPowerOverCap { get; set; }

	public double? CappedAttackPower { get; set; }
}
