using System.Collections.Generic;

namespace A2Meter.Calc;

public class FormulaConfig
{
	public int AccuracyCapMin { get; set; } = 1200;

	public int AccuracyCapMax { get; set; } = 1700;

	public double AccuracyMaxDi { get; set; } = 14.2;

	public List<double[]> AccuracyIntervals { get; set; } = new List<double[]>
	{
		new double[3] { 1200.0, 1250.0, 1.6 },
		new double[3] { 1250.0, 1300.0, 1.6 },
		new double[3] { 1300.0, 1350.0, 1.5 },
		new double[3] { 1350.0, 1400.0, 1.5 },
		new double[3] { 1400.0, 1450.0, 1.4 },
		new double[3] { 1450.0, 1500.0, 1.4 },
		new double[3] { 1500.0, 1550.0, 1.4 },
		new double[3] { 1550.0, 1600.0, 1.3 },
		new double[3] { 1600.0, 1650.0, 1.3 },
		new double[3] { 1650.0, 1700.0, 1.2 }
	};

	public double CooldownEfficiency { get; set; } = 0.3;

	public double WeaponAmpCoeff { get; set; } = 0.66;

	public double StunResistance { get; set; } = 5.0;

	public double BaseCriticalDamage { get; set; } = 1.5;

	public int TitleCritOwn { get; set; } = 80;

	public double CritStatMultiplier { get; set; } = 0.4;

	public double CritStatDivisor { get; set; } = 10.0;

	public double CritChanceCap { get; set; } = 80.0;

	public int BaseMultiHitPct { get; set; } = 18;

	public double[] MultiHitPoly { get; set; } = new double[4] { 11.1, 13.9, 17.8, 23.9 };
}
