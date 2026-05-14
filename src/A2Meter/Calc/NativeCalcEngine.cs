using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace A2Meter.Calc;

public static class NativeCalcEngine
{
	private record SkillPriorityData(string[] Active, string[] Passive, string[] Stigma);

	private class AttackPowerResult
	{
		public double FinalAttack;

		public double WeaponMinAttack;

		public double WeaponMaxAttack;

		public Dictionary<string, object> Breakdown = new Dictionary<string, object>();
	}

	private class CriticalHitResult
	{
		public double TotalCriticalHitPercent;

		public double TotalCriticalHitInteger;

		public Dictionary<string, object> Breakdown = new Dictionary<string, object>();
	}

	private class DamageAmplificationResult
	{
		public Dictionary<string, object> WeaponDamageAmp = new Dictionary<string, object>();

		public Dictionary<string, object> PveDamageAmp = new Dictionary<string, object>();

		public Dictionary<string, object> DamageAmp = new Dictionary<string, object>();

		public Dictionary<string, object> CriticalDamageAmp = new Dictionary<string, object>();
	}

	private class CombatSpeedResult
	{
		public double TotalCombatSpeed;
	}

	private class CooldownResult
	{
		public double TotalCooldownReduction;
	}

	private class StunHitResult
	{
		public double TotalStunHitPercent;
	}

	private class MultiHitResult
	{
		public double TotalMultiHitPercent;
	}

	private class PerfectResult
	{
		public double TotalPerfectPercent;
	}

	private class AccuracyResult
	{
		public double FinalAccuracyMax;

		public double TotalIntegerAccuracyMax;
	}

	private class SkillDamageResult
	{
		public double TotalSkillDamage;
	}

	private record WingEffect(double AttackPower = 0.0, double BossAttackPower = 0.0, double CriticalAttackPower = 0.0, double CriticalHit = 0.0, double DamageAmplification = 0.0, double CooldownReduction = 0.0, double AdditionalAccuracy = 0.0, double PveAccuracy = 0.0, double StunHit = 0.0)
	{
		public string Name { get; init; } = "";
	}

	private static readonly Dictionary<string, WingEffect> WingEffectsData = new Dictionary<string, WingEffect>
	{
		["공허의 탈리스라 날개"] = new WingEffect(60.0, 0.0, 0.0, 0.0, 0.0, 4.0, 35.0),
		["봄 꽃 나비의 날개"] = new WingEffect(60.0, 0.0, 0.0, 0.0, 0.0, 0.0, 35.0),
		["악몽의 날개"] = new WingEffect(60.0, 0.0, 0.0, 35.0, 3.5, 0.0, 35.0),
		["어둠의 장막 날개"] = new WingEffect(60.0, 0.0, 0.0, 0.0, 2.5),
		["숲 정령의 날개"] = new WingEffect(0.0, 95.0, 0.0, 0.0, 3.5),
		["크로메데의 날개"] = new WingEffect(60.0, 0.0, 0.0, 35.0),
		["검은 파편의 날개"] = new WingEffect(0.0, 0.0, 95.0, 35.0),
		["고대 아울라우의 날개"] = new WingEffect(60.0),
		["정복자의 날개"] = new WingEffect(0.0, 80.0),
		["보라꽃나비 날개"] = new WingEffect(40.0),
		["드라마타 둥지의 날개"] = new WingEffect(0.0, 95.0, 0.0, 0.0, 3.5, 0.0, 0.0, 45.0),
		["무아의 날개"] = new WingEffect(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 3.0)
	};

	private static readonly Dictionary<string, Dictionary<string, double>> ActiveDpsWeights = new Dictionary<string, Dictionary<string, double>>
	{
		["검성"] = new Dictionary<string, double>
		{
			["내려찍기"] = 50.0,
			["절단의 맹타"] = 30.0,
			["파멸의 맹타"] = 10.0
		},
		["살성"] = new Dictionary<string, double>
		{
			["심장 찌르기"] = 25.0,
			["문양 폭발"] = 15.0,
			["기습"] = 15.0,
			["빠른 베기"] = 15.0,
			["맹수의 포효"] = 15.0
		},
		["궁성"] = new Dictionary<string, double>
		{
			["속사"] = 35.0,
			["저격"] = 25.0,
			["조준 화살"] = 10.0,
			["광풍 화살"] = 6.0,
			["송곳 화살"] = 6.0
		},
		["정령성"] = new Dictionary<string, double>
		{
			["화염 전소"] = 44.0,
			["냉기 충격"] = 26.0,
			["원소 융합"] = 20.0
		},
		["수호성"] = new Dictionary<string, double>
		{
			["연속 난타"] = 20.0,
			["맹렬한 일격"] = 30.0,
			["심판"] = 40.0
		},
		["마도성"] = new Dictionary<string, double>
		{
			["혹한의 바람"] = 18.0,
			["불꽃 폭발"] = 20.0,
			["불꽃 화살"] = 20.0,
			["집중의 기원"] = 15.0,
			["얼음 사슬"] = 10.0
		},
		["호법성"] = new Dictionary<string, double>
		{
			["암격쇄"] = 35.0,
			["격파쇄"] = 25.0,
			["백열격"] = 10.0,
			["회전격"] = 8.0,
			["쾌유의 주문"] = 10.0
		},
		["치유성"] = new Dictionary<string, double>
		{
			["쾌유의 광휘"] = 30.0,
			["천벌"] = 30.0,
			["치유의 빛"] = 10.0,
			["재생의 빛"] = 10.0,
			["단죄"] = 10.0
		}
	};

	private static readonly Dictionary<string, Dictionary<string, double>> PassiveDpsWeights = new Dictionary<string, Dictionary<string, double>>
	{
		["검성"] = new Dictionary<string, double>
		{
			["공격 준비"] = 20.0,
			["충격 적중"] = 20.0,
			["약점 파악"] = 15.0,
			["노련한 반격"] = 15.0
		},
		["수호성"] = new Dictionary<string, double>
		{
			["격앙"] = 25.0,
			["충격 적중"] = 20.0,
			["철벽 방어"] = 15.0
		},
		["살성"] = new Dictionary<string, double>
		{
			["강습 자세"] = 25.0,
			["배후 강타"] = 25.0,
			["빈틈 노리기"] = 20.0,
			["충격 적중"] = 20.0
		},
		["궁성"] = new Dictionary<string, double>
		{
			["집중의 눈"] = 25.0,
			["사냥꾼의 결의"] = 25.0,
			["사냥꾼의 혼"] = 20.0
		},
		["마도성"] = new Dictionary<string, double>
		{
			["불꽃의 로브"] = 30.0,
			["불의 표식"] = 25.0,
			["생기 증발"] = 15.0,
			["냉기 소환"] = 15.0
		},
		["정령성"] = new Dictionary<string, double>
		{
			["정령 타격"] = 30.0,
			["정신 집중"] = 25.0,
			["침식"] = 15.0
		},
		["치유성"] = new Dictionary<string, double>
		{
			["대지의 은총"] = 25.0,
			["치유력 강화"] = 20.0,
			["주신의 은총"] = 15.0
		},
		["호법성"] = new Dictionary<string, double>
		{
			["공격 준비"] = 25.0,
			["충격 적중"] = 20.0,
			["고취의 주문"] = 15.0
		}
	};

	private static readonly double[] StigmaPositionWeights = new double[4] { 1.0, 1.0, 0.7, 0.5 };

	private static readonly Dictionary<string, SkillPriorityData> SkillPrioritiesData = new Dictionary<string, SkillPriorityData>
	{
		["검성"] = new SkillPriorityData(new string[12]
		{
			"내려찍기", "파멸의 맹타", "분쇄 파동", "절단의 맹타", "도약 찍기", "돌진 일격", "예리한 일격", "유린의 검", "발목 베기", "충격 해제",
			"검기 난무", "공중 결박"
		}, new string[10] { "공격 준비", "충격 적중", "노련한 반격", "약점 파악", "살기 파열", "생존 자세", "피의 흡수", "생존 의지", "파괴 충동", "보호의 갑옷" }, new string[12]
		{
			"돌격 자세", "지켈의 축복", "집중 막기", "근성", "격노 폭발", "분노의 파동", "균형의 갑옷", "파동의 갑주", "칼날 날리기", "강습 일격",
			"강제 결박", "흡혈의 검"
		}),
		["궁성"] = new SkillPriorityData(new string[12]
		{
			"저격", "속사", "조준 화살", "송곳 화살", "광풍 화살", "표적 화살", "파열 화살", "제압 화살", "올가미 화살", "폭발의 덫",
			"충격 해제", "화살 난사"
		}, new string[10] { "집중의 눈", "사냥꾼의 결의", "사냥꾼의 혼", "집중 포화", "속박의 눈", "경계의 눈", "근접 사격", "회생의 계약", "바람의 활력", "저항의 결의" }, new string[12]
		{
			"축복의 활", "폭발 화살", "바이젤의 권능", "기습 차기", "화살 폭풍", "은신", "대자연의 숨결", "그리폰 화살", "봉인 화살", "결박의 덫",
			"강습 강타", "수면 화살"
		}),
		["마도성"] = new SkillPriorityData(new string[12]
		{
			"불꽃 화살", "혹한의 바람", "불꽃 폭발", "얼음 사슬", "집중의 기원", "겨울의 속박", "지옥의 화염", "불꽃 작살", "빙결", "빙결 폭발",
			"화염 난사", "충격 해제"
		}, new string[10] { "불꽃의 로브", "불의 표식", "냉기 소환", "생기 증발", "냉기의 로브", "강화의 은혜", "정기 흡수", "회생의 계약", "저항의 은혜", "대지의 로브" }, new string[12]
		{
			"원소 강화", "불의 장벽", "지연 폭발", "강철 보호막", "냉기 폭풍", "빙설의 갑주", "강습 폭격", "신성 폭발", "영혼 동결", "저주: 나무",
			"빙하 강타", "루미엘의 공간"
		}),
		["살성"] = new SkillPriorityData(new string[12]
		{
			"심장 찌르기", "빠른 베기", "기습", "문양 폭발", "맹수의 포효", "폭풍 난무", "암습", "회오리 베기", "섬광 베기", "침투",
			"충격 해제", "그림자 낙하"
		}, new string[10] { "강습 자세", "배후 강타", "충격 적중", "빈틈 노리기", "방어 균열", "육감 극대화", "각오", "회생의 계약", "기습 자세", "독 바르기" }, new string[12]
		{
			"환영 분신", "트리니엘의 비수", "신속의 계약", "암검 투척", "그림자 보행", "회피 자세", "맹수의 송곳니", "나선 베기", "연막탄", "강습 습격",
			"공중 포박", "회피의 계약"
		}),
		["수호성"] = new SkillPriorityData(new string[12]
		{
			"심판", "연속 난타", "맹렬한 일격", "징벌", "비호의 일격", "쇠약의 맹타", "방패 강타", "방패 돌격", "포획", "섬멸",
			"충격 해제", "섬광 난무"
		}, new string[10] { "격앙", "충격 적중", "철벽 방어", "체력 강화", "단죄의 가호", "고통 차단", "생존 의지", "수호의 인장", "모욕의 포효", "비호의 방패" }, new string[12]
		{
			"보호의 방패", "도발", "주신의 징벌", "전우 보호", "나포", "이중 갑옷", "네자칸의 방패", "고결의 갑주", "처형의 검", "균형의 갑옷",
			"강습 맹격", "파멸의 방패"
		}),
		["정령성"] = new SkillPriorityData(new string[12]
		{
			"화염 전소", "냉기 충격", "원소 융합", "협공: 저주", "소환: 물의 정령", "공간 지배", "영혼의 절규", "소환: 바람의 정령", "연속 난사", "충격 해제",
			"소환: 불의 정령", "소환: 땅의 정령"
		}, new string[10] { "정령 타격", "정신 집중", "침식", "정령 보호", "정령 회생", "정령 강림", "원소 결집", "연속 역류", "회생의 계약", "정령 교감" }, new string[12]
		{
			"강화: 정령의 가호", "소환: 고대의 정령", "협공: 부식", "불길의 축복", "흡인", "마법 강탈", "협공: 파멸의 공세", "마법 차단", "카이시넬의 권능", "강습 공포",
			"저주의 구름", "공포의 절규"
		}),
		["치유성"] = new SkillPriorityData(new string[12]
		{
			"천벌", "쾌유의 광휘", "재생의 빛", "치유의 빛", "단죄", "대지의 응보", "신성한 기운", "고통의 연쇄", "벼락 난사", "약화의 낙인",
			"벽력", "충격 해제"
		}, new string[10] { "대지의 은총", "치유력 강화", "주신의 은총", "불사의 장막", "따뜻한 가호", "생존 의지", "찬란한 가호", "집중의 기도", "주신의 가호", "회복 차단" }, new string[12]
		{
			"보호의 빛", "구원", "대지의 징벌", "유스티엘의 권능", "소환 부활", "면죄", "증폭의 기도", "파멸의 목소리", "속박", "치유의 기운",
			"권능 폭발", "강습 낙인"
		}),
		["호법성"] = new SkillPriorityData(new string[12]
		{
			"암격쇄", "격파쇄", "백열격", "쾌유의 주문", "회전격", "타격쇄", "돌진 격파", "파동격", "진동쇄", "열파격",
			"충격 해제", "질풍 난무"
		}, new string[10] { "공격 준비", "충격 적중", "고취의 주문", "생명의 축복", "대지의 약속", "바람의 약속", "생존 의지", "보호진", "격노의 주문", "십자 방어" }, new string[12]
		{
			"불패의 진언", "질풍의 권능", "질주의 진언", "마르쿠탄의 분노", "수호의 축복", "쾌유의 손길", "분쇄격", "집중 방어", "차단의 권능", "멸화",
			"결박의 낙인", "강습 충격"
		})
	};

	private static readonly string[] WeaponDmgAmpKw = new string[3] { "무기 피해 증폭", "무기피해증폭", "weapon damage amplification" };

	private static readonly string[] PveDmgAmpKw = new string[3] { "pve 피해 증폭", "pve피해증폭", "pve damage amplification" };

	private static readonly string[] CritDmgAmpKw = new string[3] { "치명타 피해 증폭", "치명타피해증폭", "critical damage amplification" };

	private static readonly string[] DmgAmpKw = new string[2] { "피해 증폭", "damage amplification" };

	public static CalcResult RunCalc(CalcInput input)
	{
		List<JsonElement> equipment = ParseJsonArray(input.EquipmentJson);
		List<JsonElement> accessories = ParseJsonArray(input.AccessoriesJson);
		JsonElement statData = ParseJsonObject(input.StatDataJson);
		JsonElement daevanionData = ParseJsonObject(input.DaevanionDataJson);
		List<JsonElement> titles = ParseJsonArray(input.TitlesJson);
		List<JsonElement> skills = ParseJsonArray(input.SkillsJson);
		List<JsonElement> stigmas = ParseJsonArray(input.StigmasJson);
		JsonElement characterData = ParseJsonObject(input.CharacterDataJson);
		WingEffect wingEffect = GetWingEffect(input.WingName);
		AttackPowerResult attackPowerResult = CalcAttackPower(equipment, accessories, statData, daevanionData, titles, wingEffect, input.ArcanaSetCounts);
		CombatSpeedResult obj = CalcCombatSpeed(equipment, accessories, statData, daevanionData, titles);
		DamageAmplificationResult obj2 = CalcDamageAmplification(equipment, accessories, daevanionData, titles, wingEffect, input.ArcanaSetCounts);
		CriticalHitResult criticalHitResult = CalcCriticalHit(equipment, accessories, statData, daevanionData, titles, wingEffect, characterData);
		ApplyBlackShardWingEffect(wingEffect, criticalHitResult, attackPowerResult);
		CooldownResult obj3 = CalcCooldownReduction(statData, daevanionData, titles, wingEffect);
		StunHitResult obj4 = CalcStunHit(equipment, accessories, statData, titles, wingEffect);
		MultiHitResult obj5 = CalcMultiHit(equipment, accessories, daevanionData);
		PerfectResult obj6 = CalcPerfect(equipment, accessories, statData, titles);
		AccuracyResult obj7 = CalcAccuracy(equipment, accessories, statData, daevanionData, titles, wingEffect, characterData, input.JobName);
		SkillDamageResult obj8 = CalcSkillDamage(skills, stigmas, input.JobName);
		return new CalcResult
		{
			AttackPower = ToJsonElement(attackPowerResult),
			CriticalHit = ToJsonElement(criticalHitResult),
			DamageAmplification = ToJsonElement(obj2),
			CombatSpeed = ToJsonElement(obj),
			CooldownReduction = ToJsonElement(obj3),
			StunHit = ToJsonElement(obj4),
			MultiHit = ToJsonElement(obj5),
			Perfect = ToJsonElement(obj6),
			Accuracy = ToJsonElement(obj7),
			SkillDamage = ToJsonElement(obj8),
			WeaponMinAttack = attackPowerResult.WeaponMinAttack,
			WeaponMaxAttack = attackPowerResult.WeaponMaxAttack,
			IsAttackPowerOverCap = false,
			CappedAttackPower = null
		};
	}

	private static AttackPowerResult CalcAttackPower(List<JsonElement> equipment, List<JsonElement> accessories, JsonElement statData, JsonElement daevanionData, List<JsonElement> titles, WingEffect? wing, Dictionary<string, int> arcanaSetCounts)
	{
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		double num4 = 0.0;
		double num5 = 0.0;
		double num6 = 0.0;
		double num7 = 0.0;
		double num8 = 0.0;
		double weaponMinAttack = 0.0;
		double weaponMaxAttack = 0.0;
		double num9 = 0.0;
		double num10 = 0.0;
		foreach (JsonElement item3 in equipment.Where(delegate(JsonElement e)
		{
			int slotPos2 = GetSlotPos(e);
			return slotPos2 == 1 || slotPos2 == 2;
		}).ToList())
		{
			bool flag = GetSlotPos(item3) == 1;
			int num11 = GetInt(item3, "exceed_level");
			(int baseAtk, int enhanceBonus, int minAtk, int maxAtk) tuple = ExtractAttackFromMainStats(item3);
			int item = tuple.baseAtk;
			int item2 = tuple.enhanceBonus;
			int num12 = tuple.minAtk;
			int num13 = tuple.maxAtk;
			double num14 = num11 * 30;
			double num15 = num11;
			if (flag)
			{
				if (num13 == 0 && item > 0)
				{
					num13 = item + item2;
				}
				if (num12 == 0 && num13 > 0)
				{
					num12 = (int)Math.Floor((double)num13 * 0.85);
				}
				if (num13 > 0)
				{
					weaponMinAttack = num12;
					weaponMaxAttack = num13;
				}
			}
			double num18;
			if (flag && num12 > 0 && num13 > 0 && num12 < num13)
			{
				double num16 = num12 + item2;
				double num17 = item + item2;
				num18 = Math.Round((num16 + num17) / 2.0, MidpointRounding.AwayFromZero);
			}
			else
			{
				num18 = item + item2;
			}
			num += num18;
			num2 += num18;
			if (num11 > 0)
			{
				num3 += num14;
				num5 += num18 + num14;
				num4 += num15;
			}
		}
		foreach (JsonElement accessory in accessories)
		{
			int num19 = GetInt(accessory, "exceed_level");
			int num20 = 0;
			int num21 = 0;
			if (accessory.TryGetProperty("main_stats", out var value) && value.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item4 in value.EnumerateArray())
				{
					if (item4.ValueKind != JsonValueKind.Object)
					{
						continue;
					}
					string text = GetStr(item4, "name").ToLower();
					if (text.Contains("공격력") || text.Contains("attack"))
					{
						object obj = GetStatRawValue(item4, "value") ?? GetStatRawValue(item4, "minValue");
						int result;
						if (obj is int num22)
						{
							num20 += num22;
						}
						else if (obj is double num23)
						{
							num20 += (int)num23;
						}
						else if (obj is string s && int.TryParse(s, out result))
						{
							num20 += result;
						}
						object statRawValue = GetStatRawValue(item4, "extra");
						int result2;
						if (statRawValue is int num24)
						{
							num21 += num24;
						}
						else if (statRawValue is double num25)
						{
							num21 += (int)num25;
						}
						else if (statRawValue is string s2 && int.TryParse(s2, out result2))
						{
							num21 += result2;
						}
					}
				}
			}
			double num26 = num19 * 20;
			double num27 = num19;
			if (num20 > 0 || num21 > 0)
			{
				double num28 = num20 + num21;
				num += num28;
				num2 += num28;
				if (num19 > 0)
				{
					num3 += num26;
					num5 += (double)num20 + num26;
					num4 += num27;
				}
			}
			else if (num19 > 0)
			{
				num3 += num26;
				num5 += num26;
				num4 += num27;
			}
		}
		List<JsonElement> list = equipment.Concat(accessories).ToList();
		foreach (JsonElement item5 in list)
		{
			int slotPos = GetSlotPos(item5);
			bool flag2 = slotPos == 1;
			bool flag3 = slotPos == 2;
			double num29 = SumStatFromArray(item5, "sub_stats", IsAttackStat, ParseIntValue);
			if (num29 > 0.0)
			{
				num += num29;
				num2 += num29;
			}
			double num30 = SumStatFromArray(item5, "sub_stats", IsPowerStat, ParseIntValue);
			if (num30 > 0.0)
			{
				if (flag2)
				{
					num9 += num30;
				}
				else if (flag3)
				{
					num10 += num30;
				}
			}
			double num31 = SumStatFromArray(item5, "magic_stone_stat", IsAttackStat, ParseIntValue);
			if (num31 > 0.0)
			{
				num += num31;
				num2 += num31;
			}
		}
		int[] obj2 = new int[5] { 41, 42, 43, 44, 47 };
		double num32 = 0.0;
		double num33 = 0.0;
		int[] array = obj2;
		foreach (int num35 in array)
		{
			double num36 = SumDaevanionNodes(daevanionData, num35, ExtractAttackFromText, 5.0);
			if (num36 > 0.0)
			{
				num32 += num36;
				if (num35 == 47)
				{
					num33 = num36;
				}
			}
		}
		num += num32;
		double num37 = SumDaevanionNodes(daevanionData, 45, ExtractPveAttackFromText, 0.0);
		num += num37;
		int num38 = FindStat(statData, "Destruction", "파괴");
		if (num38 > 0)
		{
			num6 = (double)num38 * 0.2;
		}
		int num39 = FindStat(statData, "STR", null);
		if (num39 > 0)
		{
			num7 = (double)Math.Min(num39, 200) * 0.1;
		}
		foreach (JsonElement item6 in list)
		{
			num8 += SumStatFromArray(item6, "sub_stats", IsAttackIncreaseStat, ParseFloatValue);
		}
		double num40 = wing?.AttackPower ?? 0.0;
		double num41 = wing?.BossAttackPower ?? 0.0;
		double num42 = 0.0;
		foreach (JsonElement title in titles)
		{
			foreach (string equipEffect in GetEquipEffects(title))
			{
				Match match = Regex.Match(equipEffect, "(?:PVE\\s*공격력|추가\\s*공격력)\\s*\\+?(\\d+)", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					num42 += (double)int.Parse(match.Groups[1].Value);
				}
			}
		}
		double num43 = num32 + num2 + num3 + num40 + num42;
		double num44 = num4 + num6 + num7 + num8;
		double num45 = Math.Floor(num43 * (1.0 + num44 / 100.0)) + num37 + num41;
		int num46 = ((arcanaSetCounts.GetValueOrDefault("frenzy", 0) >= 2) ? 50 : 0);
		num45 += (double)num46;
		return new AttackPowerResult
		{
			FinalAttack = num45,
			WeaponMinAttack = weaponMinAttack,
			WeaponMaxAttack = weaponMaxAttack,
			Breakdown = new Dictionary<string, object>
			{
				["daevanionAttack"] = num32,
				["daevanionMarkutanAttack"] = num33,
				["daevanionArielAttack"] = num37,
				["equipmentAttack"] = num2,
				["equipmentAttackBase"] = num2,
				["transcendInteger"] = num3,
				["transcendPercent"] = num4,
				["destructionPercent"] = num6,
				["powerPercent"] = num7,
				["normalStatPowerPercent"] = num7,
				["soulPowerPercent"] = 0.0,
				["soulAttackIncreasePercent"] = num8,
				["wingAttackPower"] = num40,
				["wingBossAttackPower"] = num41,
				["wingName"] = ((object)wing?.Name) ?? ((object)DBNull.Value),
				["titleEquipAttackPower"] = num42,
				["arcanaFrenzyPveAttack"] = num46
			}
		};
	}

	private static CriticalHitResult CalcCriticalHit(List<JsonElement> equipment, List<JsonElement> accessories, JsonElement statData, JsonElement daevanionData, List<JsonElement> titles, WingEffect? wing, JsonElement characterData)
	{
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		double num4 = 0.0;
		foreach (JsonElement item in equipment.Where(delegate(JsonElement e)
		{
			int slotPos = GetSlotPos(e);
			return slotPos == 1 || slotPos == 2;
		}).ToList())
		{
			num += SumStatFromArray(item, "main_stats", IsCriticalStat, ParseIntValue);
		}
		foreach (JsonElement item2 in equipment.Concat(accessories).ToList())
		{
			num2 += SumStatFromArray(item2, "sub_stats", IsCriticalStat, ParseIntValue);
			num3 += SumStatFromArray(item2, "magic_stone_stat", IsCriticalStat, ParseIntValue);
		}
		int[] obj = new int[5] { 41, 42, 43, 44, 47 };
		double num5 = 0.0;
		int[] array = obj;
		foreach (int num7 in array)
		{
			double num8 = SumDaevanionNodes(daevanionData, num7, ExtractCriticalFromText, 10.0);
			num4 += num8;
			if (num7 == 47)
			{
				num5 = num8;
			}
		}
		double num9 = num + num2 + num3 + num4;
		double num10 = 0.0;
		int num11 = FindStat(statData, "Death", "죽음");
		if (num11 > 0)
		{
			num10 = (double)num11 * 0.2;
		}
		double num12 = 0.0;
		int num13 = FindStat(statData, "Accuracy", "정확");
		if (num13 > 0)
		{
			num12 = (double)Math.Min(num13, 200) * 0.1;
		}
		double num14 = wing?.CriticalHit ?? 0.0;
		num9 += num14;
		double num15 = 0.0;
		foreach (JsonElement title in titles)
		{
			foreach (string equipEffect in GetEquipEffects(title))
			{
				Match match = Regex.Match(equipEffect, "^치명타\\s*\\+?(\\d+)", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					num15 += (double)int.Parse(match.Groups[1].Value);
				}
			}
		}
		num9 += num15;
		double num16 = GetDouble(characterData, "intelligent_pet_critical_min");
		double num17 = GetDouble(characterData, "intelligent_pet_critical_max");
		if (num17 == 0.0)
		{
			num17 = 41.0;
		}
		double num18 = num10 + num12;
		return new CriticalHitResult
		{
			TotalCriticalHitInteger = num9,
			TotalCriticalHitPercent = num9 * 0.1 + num18,
			Breakdown = new Dictionary<string, object>
			{
				["baseCriticalHitInteger"] = num,
				["soulCriticalHitInteger"] = num2,
				["stoneCriticalHitInteger"] = num3,
				["daevanionCriticalHitInteger"] = num4,
				["daevanionMarkutanCriticalHitInteger"] = num5,
				["intelligentPetCriticalMin"] = num16,
				["intelligentPetCriticalMax"] = num17,
				["deathCriticalHitPercent"] = num10,
				["accuracyCriticalHitPercent"] = num12,
				["wingCriticalHitInteger"] = num14,
				["wingCriticalHitPercent"] = num14 * 0.1,
				["titleEquipCriticalHit"] = num15,
				["wingName"] = ((object)wing?.Name) ?? ((object)DBNull.Value)
			}
		};
	}

	private static void ApplyBlackShardWingEffect(WingEffect? wing, CriticalHitResult critResult, AttackPowerResult atkResult)
	{
		if (!(wing == null) && !(wing.CriticalAttackPower <= 0.0))
		{
			Dictionary<string, object> breakdown = critResult.Breakdown;
			double num = D(breakdown, "baseCriticalHitInteger") + D(breakdown, "soulCriticalHitInteger") + D(breakdown, "stoneCriticalHitInteger") + D(breakdown, "daevanionCriticalHitInteger") + D(breakdown, "wingCriticalHitInteger") + D(breakdown, "titleEquipCriticalHit");
			double num2 = D(breakdown, "intelligentPetCriticalMax");
			if (num2 == 0.0)
			{
				num2 = 41.0;
			}
			double num3 = num + (num2 + 80.0);
			double num4 = D(breakdown, "deathCriticalHitPercent");
			double num5 = D(breakdown, "accuracyCriticalHitPercent");
			double num6 = 1.0 + (num4 + num5) / 100.0;
			double num7 = Math.Min(Math.Round(num3 * num6, MidpointRounding.AwayFromZero) * 0.4 / 10.0, 80.0);
			double num8 = Math.Round(wing.CriticalAttackPower * (num7 / 100.0), MidpointRounding.AwayFromZero);
			Dictionary<string, object> breakdown2 = atkResult.Breakdown;
			double num9 = D(breakdown2, "transcendPercent");
			double num10 = D(breakdown2, "destructionPercent");
			double num11 = D(breakdown2, "powerPercent");
			double num12 = num9 + num10 + num11;
			double num13 = Math.Floor(num8 * (1.0 + num12 / 100.0));
			atkResult.FinalAttack += num13;
			atkResult.Breakdown["wingAttackPower"] = D(breakdown2, "wingAttackPower") + num8;
			atkResult.Breakdown["wingCriticalAttackPower"] = num8;
			atkResult.Breakdown["wingCriticalChance"] = num7;
		}
	}

	private static DamageAmplificationResult CalcDamageAmplification(List<JsonElement> equipment, List<JsonElement> accessories, JsonElement daevanionData, List<JsonElement> titles, WingEffect? wing, Dictionary<string, int> arcanaSetCounts)
	{
		double weapon = 0.0;
		double weaponBase = 0.0;
		double weapon2 = 0.0;
		double weapon3 = 0.0;
		double weapon4 = 0.0;
		double pve = 0.0;
		double pveBase = 0.0;
		double pve2 = 0.0;
		double pve3 = 0.0;
		double num = 0.0;
		double pve4 = 0.0;
		double dmg = 0.0;
		double num2 = 0.0;
		double dmg2 = 0.0;
		double dmg3 = 0.0;
		double dmg4 = 0.0;
		double crit = 0.0;
		double num3 = 0.0;
		double crit2 = 0.0;
		double crit3 = 0.0;
		double crit4 = 0.0;
		List<JsonElement> list = equipment.Concat(accessories).ToList();
		Dictionary<string, int> dictionary = new Dictionary<string, int>
		{
			["sip"] = 0,
			["baek"] = 0,
			["cheon"] = 0,
			["gundan"] = 0
		};
		for (int i = 0; i < list.Count; i++)
		{
			if (i != 0 || equipment.Count <= 0)
			{
				string str = GetStr(list[i], "name");
				if (str.Contains("십부장"))
				{
					dictionary["sip"]++;
				}
				if (str.Contains("백부장"))
				{
					dictionary["baek"]++;
				}
				if (str.Contains("천부장"))
				{
					dictionary["cheon"]++;
				}
				if (str.Contains("군단장"))
				{
					dictionary["gundan"]++;
				}
			}
		}
		double num4 = dictionary.Values.Sum((int c) => (c < 12) ? ((c < 8) ? ((c < 5) ? ((c >= 2) ? (-5) : 0) : (-10)) : (-15)) : (-20));
		foreach (JsonElement item in list)
		{
			ClassifyDmgAmp(item, "sub_stats", isPercent: true, ref weapon, ref pve, ref dmg, ref crit);
			ClassifyDmgAmpEquipBase(item, ref weaponBase, ref pveBase);
			ClassifyDmgAmp(item, "magic_stone_stat", isPercent: false, ref weapon2, ref pve2, ref dmg2, ref crit2);
		}
		ExtractDaevanionDmgAmp(daevanionData, 42, ref weapon3, ref pve3, ref dmg3, ref crit3);
		ExtractDaevanionDmgAmp(daevanionData, 43, ref weapon3, ref pve3, ref dmg3, ref crit3);
		ExtractDaevanionDmgAmp(daevanionData, 47, ref weapon3, ref pve3, ref dmg3, ref crit3);
		num = SumDaevanionNodes(daevanionData, 45, ExtractPveDmgAmpFromText, 0.0);
		foreach (JsonElement title in titles)
		{
			foreach (string equipEffect in GetEquipEffects(title))
			{
				string text = equipEffect.ToLower();
				if (!text.Contains("pvp 피해 증폭") && !text.Contains("pvp피해증폭"))
				{
					ClassifyDmgAmpText(equipEffect, ref weapon4, ref pve4, ref dmg4, ref crit4);
				}
			}
		}
		double num5 = wing?.DamageAmplification ?? 0.0;
		double num6 = ((arcanaSetCounts.GetValueOrDefault("purity", 0) >= 4) ? 5.0 : 0.0);
		double num7 = ((arcanaSetCounts.GetValueOrDefault("frenzy", 0) >= 4) ? 5.0 : 0.0);
		double num8 = weapon + weaponBase + weapon2 / 10.0 * 0.1 + weapon3 + weapon4;
		double num9 = pve + pveBase + pve2 / 10.0 * 0.1 + pve3 + num + pve4;
		double num10 = dmg + num2 + dmg2 / 10.0 * 0.1 + dmg3 + dmg4 + num5 + num4 + num7;
		double num11 = crit + num3 + crit2 / 10.0 * 0.1 + crit3 + crit4 + num6;
		return new DamageAmplificationResult
		{
			WeaponDamageAmp = new Dictionary<string, object> { ["totalPercent"] = num8 },
			PveDamageAmp = new Dictionary<string, object> { ["totalPercent"] = num9 },
			DamageAmp = new Dictionary<string, object> { ["totalPercent"] = num10 },
			CriticalDamageAmp = new Dictionary<string, object> { ["totalPercent"] = num11 }
		};
	}

	private static CombatSpeedResult CalcCombatSpeed(List<JsonElement> equipment, List<JsonElement> accessories, JsonElement statData, JsonElement daevanionData, List<JsonElement> titles)
	{
		double num = 0.0;
		foreach (JsonElement item in equipment.Concat(accessories))
		{
			num += SumStatFromArray(item, "sub_stats", IsCombatSpeedStat, ParseFloatValue);
		}
		foreach (JsonElement accessory in accessories)
		{
			num += SumStatFromArray(accessory, "main_stats", IsCombatSpeedStat, ParseFloatValue);
		}
		int num2 = FindStat(statData, "Time", "시간");
		if (num2 > 0)
		{
			num += (double)num2 * 0.2;
		}
		num += SumDaevanionNodes(daevanionData, 41, ExtractCombatSpeedFromText, 0.0);
		foreach (JsonElement title in titles)
		{
			foreach (string equipEffect in GetEquipEffects(title))
			{
				Match match = Regex.Match(equipEffect, "전투\\s*속도\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					num += double.Parse(match.Groups[1].Value);
				}
			}
		}
		return new CombatSpeedResult
		{
			TotalCombatSpeed = num
		};
	}

	private static CooldownResult CalcCooldownReduction(JsonElement statData, JsonElement daevanionData, List<JsonElement> titles, WingEffect? wing)
	{
		double num = 0.0;
		foreach (JsonElement title in titles)
		{
			foreach (string equipEffect in GetEquipEffects(title))
			{
				num += ExtractCooldownFromEffect(equipEffect);
			}
		}
		int num2 = FindStat(statData, "Illusion", "환상");
		if (num2 > 0)
		{
			num += (double)num2 * 0.2;
		}
		num += SumDaevanionNodes(daevanionData, 41, ExtractCooldownFromText, 0.0);
		if (wing != null)
		{
			num += wing.CooldownReduction;
		}
		return new CooldownResult
		{
			TotalCooldownReduction = num
		};
	}

	private static StunHitResult CalcStunHit(List<JsonElement> equipment, List<JsonElement> accessories, JsonElement statData, List<JsonElement> titles, WingEffect? wing)
	{
		double num = 0.0;
		foreach (JsonElement title in titles)
		{
			foreach (string equipEffect in GetEquipEffects(title))
			{
				Match match = Regex.Match(equipEffect, "강타\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					num += double.Parse(match.Groups[1].Value);
				}
			}
		}
		JsonElement el = FindStatElement(statData, "Wisdom", "지혜");
		if (el.ValueKind == JsonValueKind.Object)
		{
			int num2 = GetInt(el, "value");
			if (el.TryGetProperty("statSecondList", out var value) && value.ValueKind == JsonValueKind.Array)
			{
				int num3 = 0;
				foreach (JsonElement item in value.EnumerateArray())
				{
					string text = item.GetString() ?? "";
					if (num3 == 1 && (text.Contains("강타") || text.Contains("stun")))
					{
						num += (double)num2 * 0.1 * 2.0;
					}
					num3++;
				}
			}
		}
		foreach (JsonElement item2 in equipment.Concat(accessories))
		{
			num += SumStatFromArray(item2, "sub_stats", IsExactStunStat, ParseFloatValue);
		}
		if (wing != null)
		{
			num += wing.StunHit;
		}
		return new StunHitResult
		{
			TotalStunHitPercent = num
		};
	}

	private static MultiHitResult CalcMultiHit(List<JsonElement> equipment, List<JsonElement> accessories, JsonElement daevanionData)
	{
		double num = 0.0;
		foreach (JsonElement item in equipment.Concat(accessories))
		{
			num += SumStatFromArray(item, "sub_stats", IsMultiHitStat, ParseFloatValue);
			num += SumStatFromArray(item, "main_stats", IsMultiHitStat, ParseFloatValue);
		}
		num += SumDaevanionNodes(daevanionData, 44, ExtractMultiHitFromText, 0.0);
		return new MultiHitResult
		{
			TotalMultiHitPercent = num
		};
	}

	private static PerfectResult CalcPerfect(List<JsonElement> equipment, List<JsonElement> accessories, JsonElement statData, List<JsonElement> titles)
	{
		double num = 0.0;
		foreach (JsonElement title in titles)
		{
			foreach (string equipEffect in GetEquipEffects(title))
			{
				Match match = Regex.Match(equipEffect, "완벽\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase);
				if (match.Success && !equipEffect.Contains("완벽 저항"))
				{
					num += double.Parse(match.Groups[1].Value);
				}
			}
		}
		JsonElement el = FindStatElement(statData, "Justice", "정의");
		if (el.ValueKind == JsonValueKind.Object)
		{
			int num2 = GetInt(el, "value");
			if (el.TryGetProperty("statSecondList", out var value) && value.ValueKind == JsonValueKind.Array)
			{
				int num3 = 0;
				foreach (JsonElement item in value.EnumerateArray())
				{
					string text = item.GetString() ?? "";
					if (num3 == 1 && (text.Contains("완벽") || text.Contains("perfect")))
					{
						num += (double)num2 * 0.1 * 2.0;
					}
					num3++;
				}
			}
		}
		foreach (JsonElement accessory in accessories)
		{
			num += SumStatFromArray(accessory, "main_stats", IsExactPerfectStat, ParseFloatValue);
		}
		foreach (JsonElement item2 in equipment.Concat(accessories))
		{
			num += SumStatFromArray(item2, "sub_stats", IsExactPerfectStat, ParseFloatValue);
		}
		return new PerfectResult
		{
			TotalPerfectPercent = num
		};
	}

	private static AccuracyResult CalcAccuracy(List<JsonElement> equipment, List<JsonElement> accessories, JsonElement statData, JsonElement daevanionData, List<JsonElement> titles, WingEffect? wing, JsonElement characterData, string jobName)
	{
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		foreach (JsonElement item in equipment.Where(delegate(JsonElement e)
		{
			int slotPos = GetSlotPos(e);
			return slotPos == 1 || slotPos == 2;
		}).ToList())
		{
			num += SumStatFromArray(item, "main_stats", IsAccuracyStat, ParseIntValue);
		}
		foreach (JsonElement item2 in equipment.Concat(accessories))
		{
			num2 += SumStatFromArray(item2, "magic_stone_stat", IsAdditionalAccuracyStat, ParseIntValue);
			num3 += SumStatFromArray(item2, "sub_stats", IsExactAccuracyStat, ParseIntValue);
		}
		double num4 = 40.0;
		double num5 = 60.0;
		double num6 = wing?.AdditionalAccuracy ?? 0.0;
		double num7 = wing?.PveAccuracy ?? 0.0;
		double num8 = 0.0;
		double num9 = 0.0;
		foreach (JsonElement title in titles)
		{
			foreach (string equipEffect in GetEquipEffects(title))
			{
				Match match = Regex.Match(equipEffect, "추가\\s*명중\\s*\\+?(\\d+)", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					num8 += (double)int.Parse(match.Groups[1].Value);
				}
				Match match2 = Regex.Match(equipEffect, "PVE\\s*명중\\s*\\+?(\\d+)", RegexOptions.IgnoreCase);
				if (match2.Success)
				{
					num9 += (double)int.Parse(match2.Groups[1].Value);
				}
			}
		}
		double num10 = 0.0;
		if (jobName.Contains("검성") || jobName.Contains("호법성") || jobName.Contains("궁성") || jobName.Contains("마도성") || jobName.Contains("정령성"))
		{
			num10 = 100.0;
		}
		double num11 = GetDouble(characterData, "wild_pet_accuracy_max");
		if (num11 == 0.0)
		{
			num11 = 65.0;
		}
		double num12 = SumDaevanionNodes(daevanionData, 45, ExtractPveAccuracyFromText, 0.0);
		double num13 = SumDaevanionNodes(daevanionData, 47, ExtractAdditionalAccuracyFromText, 0.0);
		double num14 = 0.0;
		double num15 = 0.0;
		int num16 = FindStat(statData, "Freedom", "자유");
		if (num16 > 0)
		{
			num14 = (double)num16 * 0.2;
		}
		int num17 = FindStat(statData, "Accuracy", "정확");
		if (num17 > 0)
		{
			num15 = (double)Math.Min(num17, 200) * 0.1;
		}
		double num18 = num14 + num15;
		double num19 = num + num2 + num3 + num8 + num4 + num5 + num6 + num10 + num11;
		double finalAccuracyMax = Math.Floor(num19 * (1.0 + num18 / 100.0)) + num12 + num13 + num7 + num9;
		return new AccuracyResult
		{
			FinalAccuracyMax = finalAccuracyMax,
			TotalIntegerAccuracyMax = num19 + num12 + num13 + num7 + num9
		};
	}

	private static SkillDamageResult CalcSkillDamage(List<JsonElement> skills, List<JsonElement> stigmas, string jobName)
	{
		List<JsonElement> skillList = skills.Where((JsonElement s) => DetectSkillGroup(s) == "active").ToList();
		List<JsonElement> skillList2 = skills.Where((JsonElement s) => DetectSkillGroup(s) == "passive").ToList();
		if (!SkillPrioritiesData.TryGetValue(jobName, out SkillPriorityData value))
		{
			return new SkillDamageResult
			{
				TotalSkillDamage = 0.0
			};
		}
		string[] active = value.Active;
		string[] passive = value.Passive;
		string[] stigma = value.Stigma;
		double num = CalcSkillScoreFromNames(skillList, active, 12, "active", jobName);
		double num2 = CalcSkillScoreFromNames(skillList2, passive, 10, "passive", jobName);
		double num3 = CalcSkillScoreFromNames(stigmas, stigma, 4, "stigma", jobName);
		return new SkillDamageResult
		{
			TotalSkillDamage = num + num2 + num3
		};
	}

	private static double CalcSkillScoreFromNames(List<JsonElement> skillList, string[] priorityNames, int maxSlots, string skillType, string jobName)
	{
		Dictionary<string, int> skillMap = new Dictionary<string, int>();
		foreach (JsonElement skill in skillList)
		{
			string str = GetStr(skill, "name");
			if (string.IsNullOrEmpty(str))
			{
				str = GetStr(skill, "skillName");
			}
			int num = GetInt(skill, "level_int");
			if (num == 0)
			{
				num = GetInt(skill, "skillLevel");
			}
			if (num > 0 && !string.IsNullOrEmpty(str))
			{
				skillMap.TryAdd(str, num);
			}
		}
		string[] array = ((!(skillType == "stigma")) ? priorityNames.Take(maxSlots).ToArray() : (from x in (from n in priorityNames
				select (name: n, lv: skillMap.GetValueOrDefault(n, 0)) into x
				where x.lv > 0
				orderby x.lv descending
				select x).Take(4)
			select x.name).ToArray());
		double num2 = 0.0;
		for (int num3 = 0; num3 < array.Length; num3++)
		{
			string text = array[num3];
			int valueOrDefault = skillMap.GetValueOrDefault(text, 0);
			if (valueOrDefault == 0)
			{
				continue;
			}
			double num4 = (double)valueOrDefault * 1.35;
			double num5 = 0.0;
			if (skillType == "active")
			{
				if (valueOrDefault >= 8)
				{
					num5 += 5.0;
				}
				if (valueOrDefault >= 12)
				{
					num5 += 10.0;
				}
				if (valueOrDefault >= 16)
				{
					num5 += 15.0;
				}
				if (valueOrDefault >= 20)
				{
					num5 += 10.0;
				}
			}
			else if (skillType == "stigma")
			{
				if (valueOrDefault >= 5)
				{
					num5 += 5.0;
				}
				if (valueOrDefault >= 10)
				{
					num5 += 10.0;
				}
				if (valueOrDefault >= 15)
				{
					num5 += 20.0;
				}
				if (valueOrDefault >= 20)
				{
					num5 += 40.0;
				}
			}
			double num6 = 1.0;
			switch (skillType)
			{
			case "active":
				num6 = GetSkillDpsMultiplier(text, jobName, maxSlots, ActiveDpsWeights);
				break;
			case "passive":
				num6 = GetSkillDpsMultiplier(text, jobName, maxSlots, PassiveDpsWeights);
				break;
			case "stigma":
				num6 = ((num3 < StigmaPositionWeights.Length) ? StigmaPositionWeights[num3] : 0.5);
				break;
			}
			num2 += (num4 + num5) * num6;
		}
		return num2 / (double)maxSlots;
	}

	private static double GetSkillDpsMultiplier(string skillName, string classJob, int maxSlots, Dictionary<string, Dictionary<string, double>> weightTable)
	{
		if (string.IsNullOrEmpty(classJob) || !weightTable.TryGetValue(classJob, out Dictionary<string, double> value))
		{
			return 1.0;
		}
		double num = value.Values.Sum();
		int count = value.Count;
		int num2 = maxSlots - count;
		double num3 = 100.0 - num;
		double defaultValue = ((num2 > 0) ? (num3 / (double)num2) : 0.0);
		return value.GetValueOrDefault(skillName, defaultValue) / 100.0 * (double)maxSlots;
	}

	private static List<JsonElement> ParseJsonArray(string json)
	{
		if (string.IsNullOrEmpty(json) || json == "null" || json == "[]")
		{
			return new List<JsonElement>();
		}
		try
		{
			JsonDocument jsonDocument = JsonDocument.Parse(json);
			if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
			{
				return jsonDocument.RootElement.EnumerateArray().ToList();
			}
		}
		catch
		{
		}
		return new List<JsonElement>();
	}

	private static JsonElement ParseJsonObject(string json)
	{
		if (string.IsNullOrEmpty(json) || json == "null" || json == "{}")
		{
			return default(JsonElement);
		}
		try
		{
			return JsonDocument.Parse(json).RootElement;
		}
		catch
		{
			return default(JsonElement);
		}
	}

	private static int GetSlotPos(JsonElement item)
	{
		if (item.TryGetProperty("slotPos", out var value) && value.ValueKind == JsonValueKind.Number)
		{
			return value.GetInt32();
		}
		return 0;
	}

	private static int GetInt(JsonElement el, string prop)
	{
		if (el.ValueKind != JsonValueKind.Object)
		{
			return 0;
		}
		if (!el.TryGetProperty(prop, out var value))
		{
			return 0;
		}
		if (value.ValueKind == JsonValueKind.Number)
		{
			if (!value.TryGetInt32(out var value2))
			{
				return (int)value.GetDouble();
			}
			return value2;
		}
		if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var result))
		{
			return result;
		}
		return 0;
	}

	private static double GetDouble(JsonElement el, string prop)
	{
		if (el.ValueKind != JsonValueKind.Object)
		{
			return 0.0;
		}
		if (!el.TryGetProperty(prop, out var value))
		{
			return 0.0;
		}
		if (value.ValueKind == JsonValueKind.Number)
		{
			return value.GetDouble();
		}
		if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var result))
		{
			return result;
		}
		return 0.0;
	}

	private static string GetStr(JsonElement el, string prop)
	{
		if (el.ValueKind != JsonValueKind.Object)
		{
			return "";
		}
		if (!el.TryGetProperty(prop, out var value))
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static double D(Dictionary<string, object> dict, string key)
	{
		if (!dict.TryGetValue(key, out object value))
		{
			return 0.0;
		}
		return Convert.ToDouble(value);
	}

	private static (int baseAtk, int enhanceBonus, int minAtk, int maxAtk) ExtractAttackFromMainStats(JsonElement item)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		if (!item.TryGetProperty("main_stats", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return (baseAtk: 0, enhanceBonus: 0, minAtk: 0, maxAtk: 0);
		}
		foreach (JsonElement item2 in value.EnumerateArray())
		{
			string text = (GetStr(item2, "name") + " " + GetStr(item2, "id")).ToLower();
			if (!text.Contains("공격력") && !text.Contains("attack"))
			{
				continue;
			}
			object statRawValue = GetStatRawValue(item2, "minValue");
			if (statRawValue is int num5)
			{
				num3 += num5;
			}
			else if (statRawValue is string input)
			{
				Match match = Regex.Match(input, "(\\d+)\\s*\\(\\+(\\d+)\\)");
				int result;
				if (match.Success)
				{
					num3 += int.Parse(match.Groups[1].Value);
					num3 += int.Parse(match.Groups[2].Value);
				}
				else if (int.TryParse(Regex.Replace(input, "[^\\d]", ""), out result) && result > 0)
				{
					num3 += result;
				}
			}
			object statRawValue2 = GetStatRawValue(item2, "value");
			if (statRawValue2 is int num6)
			{
				num += num6;
				num4 += num6;
			}
			else if (statRawValue2 is string input2)
			{
				Match match2 = Regex.Match(input2, "(\\d+)\\s*\\(\\+(\\d+)\\)");
				int result2;
				if (match2.Success)
				{
					int num7 = int.Parse(match2.Groups[1].Value);
					int num8 = int.Parse(match2.Groups[2].Value);
					num += num7;
					num2 += num8;
					num4 += num7 + num8;
				}
				else if (int.TryParse(Regex.Replace(input2, "[^\\d]", ""), out result2) && result2 > 0)
				{
					num += result2;
					num4 += result2;
				}
			}
			object statRawValue3 = GetStatRawValue(item2, "extra");
			int result3;
			if (statRawValue3 is int num9 && num9 > 0)
			{
				num2 += num9;
			}
			else if (statRawValue3 is string text2 && text2 != "0" && text2 != "0%" && int.TryParse(Regex.Replace(text2, "[^\\d]", ""), out result3) && result3 > 0)
			{
				num2 += result3;
			}
		}
		return (baseAtk: num, enhanceBonus: num2, minAtk: num3, maxAtk: num4);
	}

	private static object? GetStatRawValue(JsonElement stat, string prop)
	{
		if (!stat.TryGetProperty(prop, out var value))
		{
			return null;
		}
		if (value.ValueKind == JsonValueKind.Number)
		{
			if (value.TryGetInt32(out var value2))
			{
				return value2;
			}
			return value.GetDouble();
		}
		if (value.ValueKind == JsonValueKind.String)
		{
			return value.GetString();
		}
		return null;
	}

	private static double SumStatFromArray(JsonElement item, string arrayProp, Func<string, bool> nameFilter, Func<JsonElement, double> valueExtractor)
	{
		if (!item.TryGetProperty(arrayProp, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return 0.0;
		}
		double num = 0.0;
		foreach (JsonElement item2 in value.EnumerateArray())
		{
			if (item2.ValueKind == JsonValueKind.Object)
			{
				string text = GetStr(item2, "name").Trim();
				if (string.IsNullOrEmpty(text))
				{
					text = GetStr(item2, "type").Trim();
				}
				if (string.IsNullOrEmpty(text))
				{
					text = GetStr(item2, "id").Trim();
				}
				string arg = text.ToLower();
				if (nameFilter(arg))
				{
					num += valueExtractor(item2);
				}
			}
		}
		return num;
	}

	private static double ParseIntValue(JsonElement stat)
	{
		object obj = GetStatRawValue(stat, "value") ?? GetStatRawValue(stat, "amount");
		if (obj is int num)
		{
			return num;
		}
		if (obj is double num2)
		{
			return (int)num2;
		}
		if (obj is string text)
		{
			if (int.TryParse(text, out var result))
			{
				return result;
			}
			Match match = Regex.Match(text, "(\\d+)");
			if (match.Success)
			{
				return int.Parse(match.Groups[1].Value);
			}
		}
		return 0.0;
	}

	private static double ParseFloatValue(JsonElement stat)
	{
		object obj = GetStatRawValue(stat, "value") ?? GetStatRawValue(stat, "amount");
		if (obj is int num)
		{
			return num;
		}
		if (obj is double)
		{
			return (double)obj;
		}
		if (obj is string text)
		{
			if (double.TryParse(text.Replace("%", "").Trim(), out var result))
			{
				return result;
			}
			Match match = Regex.Match(text, "(\\d+\\.?\\d*)");
			if (match.Success && double.TryParse(match.Groups[1].Value, out var result2))
			{
				return result2;
			}
		}
		return 0.0;
	}

	private static bool IsAttackStat(string name)
	{
		if (!name.Contains("공격력"))
		{
			return name.Contains("attack");
		}
		return true;
	}

	private static bool IsAttackIncreaseStat(string name)
	{
		return name.Trim() == "공격력 증가";
	}

	private static bool IsPowerStat(string name)
	{
		if (!name.Contains("위력") && !name.Contains("power"))
		{
			return name.Contains("might");
		}
		return true;
	}

	private static bool IsCriticalStat(string name)
	{
		if ((name.Contains("치명타") || name.Contains("critical")) && !name.Contains("치명타 방어력") && !name.Contains("치명타 저항") && !name.Contains("치명타 피해 증폭") && !name.Contains("critical resistance"))
		{
			return !name.Contains("critical damage");
		}
		return false;
	}

	private static bool IsCombatSpeedStat(string name)
	{
		if (!name.Contains("전투 속도") && !name.Contains("전투속도") && !name.Contains("combat speed"))
		{
			return name.Contains("combatspeed");
		}
		return true;
	}

	private static bool IsMultiHitStat(string name)
	{
		if (!name.Contains("다단히트") && !name.Contains("다단 히트"))
		{
			if (name.Contains("multi"))
			{
				return name.Contains("hit");
			}
			return false;
		}
		return true;
	}

	private static bool IsExactStunStat(string name)
	{
		if (!(name.Trim() == "강타"))
		{
			return name.Trim() == "stun";
		}
		return true;
	}

	private static bool IsExactPerfectStat(string name)
	{
		string text = name.Trim();
		if (!(text == "완벽") && !(text == "perfect") && (!text.StartsWith("완벽 ") || text.Contains("완벽 저항")))
		{
			if (text.StartsWith("perfect "))
			{
				return !text.Contains("perfect resistance");
			}
			return false;
		}
		return true;
	}

	private static bool IsAccuracyStat(string name)
	{
		if (!(name == "명중"))
		{
			if (name.Contains("명중") && !name.Contains("추가 명중"))
			{
				return !name.Contains("다단");
			}
			return false;
		}
		return true;
	}

	private static bool IsAdditionalAccuracyStat(string name)
	{
		if (!name.Contains("추가 명중"))
		{
			return name == "추가명중";
		}
		return true;
	}

	private static bool IsExactAccuracyStat(string name)
	{
		if ((name.Trim() == "명중" || name.Trim() == "accuracy") && !name.Contains("추가"))
		{
			return !name.Contains("다단");
		}
		return false;
	}

	private static double SumDaevanionNodes(JsonElement daevanionData, int boardId, Func<string, double> textExtractor, double defaultForEmptyNode)
	{
		if (daevanionData.ValueKind != JsonValueKind.Object)
		{
			return 0.0;
		}
		if (!daevanionData.TryGetProperty(boardId.ToString(), out var value))
		{
			return 0.0;
		}
		if (!value.TryGetProperty("nodeList", out var value2) || value2.ValueKind != JsonValueKind.Array)
		{
			return 0.0;
		}
		double num = 0.0;
		foreach (JsonElement item in value2.EnumerateArray())
		{
			if (!IsNodeActive(item))
			{
				continue;
			}
			double num2 = ExtractFromNode(item, textExtractor);
			if (num2 == 0.0 && defaultForEmptyNode > 0.0)
			{
				string text = GetStr(item, "name") + GetStr(item, "desc") + GetStr(item, "effect");
				if ((Delegate?)textExtractor == (Delegate?)new Func<string, double>(ExtractAttackFromText) && text.Contains("공격력") && !text.Contains("PVE") && !text.Contains("pve") && !text.Contains("보스") && !text.Contains("무기"))
				{
					num2 = defaultForEmptyNode;
				}
				else if ((Delegate?)textExtractor == (Delegate?)new Func<string, double>(ExtractCriticalFromText) && text.Contains("치명타") && !text.Contains("치명타 저항") && !text.Contains("치명타 방어력") && !text.Contains("치명타 피해") && !text.Contains("저항"))
				{
					num2 = defaultForEmptyNode;
				}
			}
			num += num2;
		}
		return num;
	}

	private static bool IsNodeActive(JsonElement node)
	{
		if (!node.TryGetProperty("open", out var value))
		{
			return false;
		}
		if (value.ValueKind == JsonValueKind.Number)
		{
			return value.GetInt32() == 1;
		}
		if (value.ValueKind == JsonValueKind.True)
		{
			return true;
		}
		if (value.ValueKind == JsonValueKind.String)
		{
			if (!(value.GetString() == "1"))
			{
				return value.GetString() == "true";
			}
			return true;
		}
		return false;
	}

	private static double ExtractFromNode(JsonElement node, Func<string, double> textExtractor)
	{
		double num = 0.0;
		foreach (JsonProperty item in node.EnumerateObject())
		{
			if (!(item.Name == "open") && !(item.Name == "nodeId") && !(item.Name == "order"))
			{
				num += ExtractFromValue(item.Value, textExtractor);
			}
		}
		return num;
	}

	private static double ExtractFromValue(JsonElement value, Func<string, double> textExtractor)
	{
		double num = 0.0;
		if (value.ValueKind == JsonValueKind.String)
		{
			string text = value.GetString() ?? "";
			if (!string.IsNullOrWhiteSpace(text))
			{
				num += textExtractor(text);
			}
		}
		else if (value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.Object)
				{
					foreach (JsonProperty item2 in item.EnumerateObject())
					{
						num += ExtractFromValue(item2.Value, textExtractor);
					}
				}
				else if (item.ValueKind == JsonValueKind.String)
				{
					string text2 = item.GetString() ?? "";
					if (!string.IsNullOrWhiteSpace(text2))
					{
						num += textExtractor(text2);
					}
				}
			}
		}
		else if (value.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item3 in value.EnumerateObject())
			{
				num += ExtractFromValue(item3.Value, textExtractor);
			}
		}
		return num;
	}

	private static double ExtractAttackFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "(?:추가\\s*)?공격력\\s*[+＋]\\s*(\\d+)", RegexOptions.IgnoreCase))
		{
			string text2 = text.Substring(0, Math.Max(0, item.Index)).ToLower();
			if (!text2.EndsWith("pve ") && !text2.EndsWith("pve") && !text2.EndsWith("보스 ") && !text2.EndsWith("보스") && !text2.EndsWith("무기 ") && !text2.EndsWith("무기"))
			{
				num += (double)int.Parse(item.Groups[1].Value);
			}
		}
		return num;
	}

	private static double ExtractPveAttackFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "(?:PVE\\s*공격력|보스\\s*공격력)\\s*[+＋]\\s*(\\d+)", RegexOptions.IgnoreCase))
		{
			num += (double)int.Parse(item.Groups[1].Value);
		}
		return num;
	}

	private static double ExtractCriticalFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "치명타\\s*[+＋]\\s*(\\d+)", RegexOptions.IgnoreCase))
		{
			if (!text.Contains("치명타 저항") && !text.Contains("치명타 방어력") && !text.Contains("치명타 피해 증폭"))
			{
				num += (double)int.Parse(item.Groups[1].Value);
			}
		}
		return num;
	}

	private static double ExtractCombatSpeedFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "전투\\s*속도\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			num += double.Parse(item.Groups[1].Value);
		}
		return num;
	}

	private static double ExtractCooldownFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "(?:재사용\\s*시간\\s*감소|재사용시간감소|재사용\\s*대기\\s*시간\\s*감소|재시전\\s*시간\\s*감소|cooldown\\s*reduction)\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			num += double.Parse(item.Groups[1].Value);
		}
		return num;
	}

	private static double ExtractCooldownFromEffect(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "(?:재사용\\s*시간\\s*감소|재사용시간감소|재사용\\s*대기\\s*시간\\s*감소|재시전\\s*시간\\s*감소|cooldown\\s*reduction)\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			num += double.Parse(item.Groups[1].Value);
		}
		return num;
	}

	private static double ExtractMultiHitFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "다단\\s*히트\\s*적중\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			num += double.Parse(item.Groups[1].Value);
		}
		return num;
	}

	private static double ExtractPveDmgAmpFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "(?:PVE\\s*피해\\s*증폭|보스\\s*피해\\s*증폭|보스\\s*피해\\s*증가)\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			num += double.Parse(item.Groups[1].Value);
		}
		return num;
	}

	private static double ExtractPveAccuracyFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "PVE\\s*명중\\s*[+＋]\\s*(\\d+)", RegexOptions.IgnoreCase))
		{
			num += (double)int.Parse(item.Groups[1].Value);
		}
		return num;
	}

	private static double ExtractAdditionalAccuracyFromText(string text)
	{
		double num = 0.0;
		foreach (Match item in Regex.Matches(text, "(?:추가\\s*)?명중\\s*[+＋]\\s*(\\d+)", RegexOptions.IgnoreCase))
		{
			if (!text.ToLower().Contains("pve") && !text.Contains("다단"))
			{
				num += (double)int.Parse(item.Groups[1].Value);
			}
		}
		return num;
	}

	private static void ClassifyDmgAmp(JsonElement item, string arrayProp, bool isPercent, ref double weapon, ref double pve, ref double dmg, ref double crit)
	{
		if (!item.TryGetProperty(arrayProp, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item2 in value.EnumerateArray())
		{
			if (item2.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			string text = GetStr(item2, "name").Trim();
			if (string.IsNullOrEmpty(text))
			{
				text = GetStr(item2, "type").Trim();
			}
			string name = text.ToLower();
			double num = (isPercent ? ParseFloatValue(item2) : ParseIntValue(item2));
			if (!(num <= 0.0))
			{
				if (WeaponDmgAmpKw.Any((string k) => name.Contains(k.ToLower())))
				{
					weapon += num;
				}
				else if (PveDmgAmpKw.Any((string k) => name.Contains(k.ToLower())))
				{
					pve += num;
				}
				else if (CritDmgAmpKw.Any((string k) => name.Contains(k.ToLower())))
				{
					crit += num;
				}
				else if (DmgAmpKw.Any((string k) => name.Contains(k.ToLower())) && !name.Contains("무기") && !name.Contains("pve") && !name.Contains("치명타") && !name.Contains("weapon") && !name.Contains("critical"))
				{
					dmg += num;
				}
			}
		}
	}

	private static void ClassifyDmgAmpEquipBase(JsonElement item, ref double weaponBase, ref double pveBase)
	{
		if (!item.TryGetProperty("main_stats", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item2 in value.EnumerateArray())
		{
			if (item2.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			string name = GetStr(item2, "name").ToLower();
			double num = 0.0;
			object statRawValue = GetStatRawValue(item2, "value");
			if (statRawValue is string input)
			{
				Match match = Regex.Match(input, "(\\d+\\.?\\d*)\\s*%?\\s*\\(\\+\\s*(\\d+\\.?\\d*)\\s*%?\\)");
				if (match.Success)
				{
					num = double.Parse(match.Groups[1].Value) + double.Parse(match.Groups[2].Value);
				}
				else
				{
					Match match2 = Regex.Match(input, "(\\d+\\.?\\d*)");
					if (match2.Success)
					{
						num = double.Parse(match2.Groups[1].Value);
					}
				}
			}
			else if (statRawValue is int num2)
			{
				num = num2;
			}
			object statRawValue2 = GetStatRawValue(item2, "extra");
			if (statRawValue2 is string text && text != "0" && text != "0%")
			{
				Match match3 = Regex.Match(text, "\\+?\\s*(\\d+\\.?\\d*)\\s*%?");
				if (match3.Success)
				{
					num += double.Parse(match3.Groups[1].Value);
				}
			}
			else if (statRawValue2 is int num3)
			{
				num += (double)num3;
			}
			if (num > 0.0)
			{
				if (WeaponDmgAmpKw.Any((string k) => name.Contains(k.ToLower())))
				{
					weaponBase += num;
				}
				else if (PveDmgAmpKw.Any((string k) => name.Contains(k.ToLower())))
				{
					pveBase += num;
				}
			}
		}
	}

	private static void ExtractDaevanionDmgAmp(JsonElement daevanionData, int boardId, ref double weapon, ref double pve, ref double dmg, ref double crit)
	{
		if (daevanionData.ValueKind != JsonValueKind.Object || !daevanionData.TryGetProperty(boardId.ToString(), out var value) || !value.TryGetProperty("nodeList", out var value2) || value2.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		foreach (JsonElement item in value2.EnumerateArray())
		{
			if (!IsNodeActive(item))
			{
				continue;
			}
			foreach (JsonProperty item2 in item.EnumerateObject())
			{
				if (item2.Value.ValueKind == JsonValueKind.String)
				{
					ClassifyDmgAmpText(item2.Value.GetString() ?? "", ref weapon, ref pve, ref dmg, ref crit);
				}
				else
				{
					if (item2.Value.ValueKind != JsonValueKind.Array)
					{
						continue;
					}
					foreach (JsonElement item3 in item2.Value.EnumerateArray())
					{
						if (item3.ValueKind != JsonValueKind.Object)
						{
							continue;
						}
						foreach (JsonProperty item4 in item3.EnumerateObject())
						{
							if (item4.Value.ValueKind == JsonValueKind.String)
							{
								ClassifyDmgAmpText(item4.Value.GetString() ?? "", ref weapon, ref pve, ref dmg, ref crit);
							}
						}
					}
				}
			}
		}
	}

	private static void ClassifyDmgAmpText(string text, ref double weapon, ref double pve, ref double dmg, ref double crit)
	{
		foreach (Match item in Regex.Matches(text, "무기\\s*피해\\s*증폭\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			weapon += double.Parse(item.Groups[1].Value);
		}
		foreach (Match item2 in Regex.Matches(text, "pve\\s*피해\\s*증폭\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			pve += double.Parse(item2.Groups[1].Value);
		}
		foreach (Match item3 in Regex.Matches(text, "치명타\\s*피해\\s*증폭\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			crit += double.Parse(item3.Groups[1].Value);
		}
		foreach (Match item4 in Regex.Matches(text, "피해\\s*증폭\\s*[+＋]\\s*(\\d+\\.?\\d*)\\s*%", RegexOptions.IgnoreCase))
		{
			string text2 = text.ToLower();
			if (!text2.Contains("무기") && !text2.Contains("pve") && !text2.Contains("치명타") && !text2.Contains("weapon") && !text2.Contains("critical"))
			{
				dmg += double.Parse(item4.Groups[1].Value);
			}
		}
	}

	private static int FindStat(JsonElement statData, string type, string? nameContains)
	{
		JsonElement el = FindStatElement(statData, type, nameContains);
		if (el.ValueKind != JsonValueKind.Object)
		{
			return 0;
		}
		return GetInt(el, "value");
	}

	private static JsonElement FindStatElement(JsonElement statData, string type, string? nameContains)
	{
		if (statData.ValueKind != JsonValueKind.Object)
		{
			return default(JsonElement);
		}
		if (!statData.TryGetProperty("statList", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return default(JsonElement);
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			string str = GetStr(item, "type");
			string str2 = GetStr(item, "name");
			if (str == type)
			{
				return item;
			}
			if (nameContains != null && str2.Contains(nameContains))
			{
				return item;
			}
		}
		return default(JsonElement);
	}

	private static WingEffect? GetWingEffect(string wingName)
	{
		if (string.IsNullOrEmpty(wingName))
		{
			return null;
		}
		foreach (var (text2, wingEffect2) in WingEffectsData)
		{
			if (wingName.Contains(text2) || text2.Contains(wingName))
			{
				return wingEffect2 with
				{
					Name = text2
				};
			}
		}
		return null;
	}

	private static List<string> GetEquipEffects(JsonElement title)
	{
		List<string> list = new List<string>();
		if (title.TryGetProperty("equip_effects", out var value) && value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value.EnumerateArray())
			{
				string text = item.GetString();
				if (!string.IsNullOrEmpty(text))
				{
					list.Add(text);
				}
			}
		}
		return list;
	}

	private static string DetectSkillGroup(JsonElement skill)
	{
		string text = (GetStr(skill, "group") + " " + GetStr(skill, "skill_group")).ToLower();
		if (text.Contains("passive") || text.Contains("패시브"))
		{
			return "passive";
		}
		string text2 = GetStr(skill, "type").ToLower();
		if (text2.Contains("passive") || text2.Contains("패시브"))
		{
			return "passive";
		}
		return "active";
	}

	private static JsonElement ToJsonElement(object obj)
	{
		try
		{
			string json = ((obj is AttackPowerResult { FinalAttack: var finalAttack } attackPowerResult) ? JsonSerializer.Serialize(new
			{
				finalAttack = finalAttack,
				integerAttack = 0,
				percentAttack = 0,
				breakdown = attackPowerResult.Breakdown.Where<KeyValuePair<string, object>>((KeyValuePair<string, object> kv) => !(kv.Value is DBNull)).ToDictionary((KeyValuePair<string, object> kv) => kv.Key, (KeyValuePair<string, object> kv) => kv.Value)
			}) : ((obj is CriticalHitResult { TotalCriticalHitPercent: var totalCriticalHitPercent, TotalCriticalHitInteger: var totalCriticalHitInteger } criticalHitResult) ? JsonSerializer.Serialize(new
			{
				totalCriticalHitPercent = totalCriticalHitPercent,
				totalCriticalHitInteger = totalCriticalHitInteger,
				breakdown = criticalHitResult.Breakdown.Where<KeyValuePair<string, object>>((KeyValuePair<string, object> kv) => !(kv.Value is DBNull)).ToDictionary((KeyValuePair<string, object> kv) => kv.Key, (KeyValuePair<string, object> kv) => kv.Value)
			}) : ((obj is DamageAmplificationResult damageAmplificationResult) ? JsonSerializer.Serialize(new
			{
				weaponDamageAmp = damageAmplificationResult.WeaponDamageAmp,
				pveDamageAmp = damageAmplificationResult.PveDamageAmp,
				damageAmp = damageAmplificationResult.DamageAmp,
				criticalDamageAmp = damageAmplificationResult.CriticalDamageAmp
			}) : ((obj is CombatSpeedResult combatSpeedResult) ? JsonSerializer.Serialize(new
			{
				totalCombatSpeed = combatSpeedResult.TotalCombatSpeed
			}) : ((obj is CooldownResult cooldownResult) ? JsonSerializer.Serialize(new
			{
				totalCooldownReduction = cooldownResult.TotalCooldownReduction
			}) : ((obj is StunHitResult stunHitResult) ? JsonSerializer.Serialize(new
			{
				totalStunHitPercent = stunHitResult.TotalStunHitPercent
			}) : ((obj is MultiHitResult multiHitResult) ? JsonSerializer.Serialize(new
			{
				totalMultiHitPercent = multiHitResult.TotalMultiHitPercent
			}) : ((obj is PerfectResult perfectResult) ? JsonSerializer.Serialize(new
			{
				totalPerfectPercent = perfectResult.TotalPerfectPercent
			}) : ((obj is AccuracyResult accuracyResult) ? JsonSerializer.Serialize(new
			{
				finalAccuracyMax = accuracyResult.FinalAccuracyMax,
				totalIntegerAccuracyMax = accuracyResult.TotalIntegerAccuracyMax
			}) : ((!(obj is SkillDamageResult skillDamageResult)) ? "null" : JsonSerializer.Serialize(new
			{
				totalSkillDamage = skillDamageResult.TotalSkillDamage
			})))))))))));
			return JsonDocument.Parse(json).RootElement;
		}
		catch
		{
			return default(JsonElement);
		}
	}
}
