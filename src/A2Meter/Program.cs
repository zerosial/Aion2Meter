using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;
using A2Meter.Forms;
using Vortice.Mathematics;

namespace A2Meter;

internal static class Program
{
	private static Mutex? _mutex;

	[STAThread]
	private static void Main(string[] args)
	{
		var tuple = ParseArgs(args);
		if (tuple.Demo)
		{
			RunDemo();
			return;
		}
		var (text, realtime, speed, _, admin) = tuple;
		var exePath = Environment.ProcessPath;
		if (!string.IsNullOrEmpty(exePath))
		{
			var exeName = Path.GetFileNameWithoutExtension(exePath);
			if (exeName.Contains("admin", StringComparison.OrdinalIgnoreCase))
			{
				admin = true;
			}
		}
		AppSettings.Instance.AdminMode = admin;
		if (admin)
		{
			ExtractAdminTool();
		}
		if (text == null)
		{
			_mutex = new Mutex(initiallyOwned: true, "A2Meter.SingleInstance.Mutex", out var createdNew);
			if (!createdNew)
			{
				return;
			}
		}
		ApplicationConfiguration.Initialize();
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		AppSettings settings = AppSettings.Instance;
		OverlayForm overlay = new OverlayForm();
		try
		{
			if (text != null)
			{
				overlay.PacketSourceOverride = new PcapReplaySource(text, realtime, speed);
				overlay.Text = "A2Meter [replay: " + Path.GetFileName(text) + "]";
				overlay.Tag = Path.GetFileName(text);
			}
			overlay.HandleCreated += delegate
			{
				HotkeyManager hotkeyManager = new HotkeyManager(overlay);
				overlay.Hotkeys = hotkeyManager;
				hotkeyManager.RegisterFromSettings(settings.Shortcuts);
				Task.Run(async delegate
				{
					(Version, string, string)? tuple3 = await AutoUpdater.CheckAsync(delegate(string msg)
					{
						Console.Error.WriteLine(msg);
					});
					if (tuple3.HasValue)
					{
						var (ver, url, notes) = tuple3.Value;
						overlay.Invoke(delegate
						{
							new UpdateToastForm(overlay, ver, url, notes).Show();
						});
					}
				});
			};
			using (new TrayManager(overlay, () => settings.OverlayOnlyWhenAion, delegate(bool v)
			{
				settings.OverlayOnlyWhenAion = v;
				settings.SaveDebounced();
			}))
			{
				overlay.AppCloseRequested += delegate
				{
					settings.Save();
					Application.Exit();
				};
				Application.Run(overlay);
			}
		}
		finally
		{
			if (overlay != null)
			{
				((IDisposable)overlay).Dispose();
			}
		}
	}

	private static void RunDemo()
	{
		ApplicationConfiguration.Initialize();
		Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		DpsCanvas canvas = new DpsCanvas
		{
			Dock = DockStyle.Fill
		};
		Form form = new Form
		{
			Text = "A2Meter [demo]",
			FormBorderStyle = FormBorderStyle.Sizable,
			StartPosition = FormStartPosition.CenterScreen,
			Size = new System.Drawing.Size(460, 500),
			BackColor = System.Drawing.Color.FromArgb(8, 11, 20)
		};
		form.Controls.Add(canvas);
		List<DpsCanvas.SkillBar> skills = new List<DpsCanvas.SkillBar>
		{
			new DpsCanvas.SkillBar("철벽 방어", 3200000L, 142L, 0.35, 0.38, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0L),
			new DpsCanvas.SkillBar("심판의 일격", 2100000L, 98L, 0.42, 0.25, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0L),
			new DpsCanvas.SkillBar("도발 강타", 1500000L, 76L, 0.28, 0.18, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0L),
			new DpsCanvas.SkillBar("방패 돌진", 980000L, 54L, 0.31, 0.12, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0L),
			new DpsCanvas.SkillBar("수호의 맹세", 450000L, 32L, 0.15, 0.05, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0L)
		};
		List<DpsCanvas.PlayerRow> rows = new List<DpsCanvas.PlayerRow>
		{
			new DpsCanvas.PlayerRow("수호성", "수호성", 8450000L, 1.0, 352083L, 0.34, 0L, new Color4(0.49f, 0.627f, 0.976f), skills, 42000, 410000, 352083L, 120000L, 0L),
			new DpsCanvas.PlayerRow("살성", "살성", 7820000L, 0.93, 325833L, 0.48, 0L, new Color4(0.643f, 0.906f, 0.608f), null, 38500, 398000, 325833L, 0L, 0L),
			new DpsCanvas.PlayerRow("마도성", "마도성", 6950000L, 0.82, 289583L, 0.41, 0L, new Color4(0.718f, 0.549f, 0.949f), null, 35200, 372000, 289583L, 1200000L, 0L),
			new DpsCanvas.PlayerRow("치유성", "치유성", 2180000L, 0.26, 90833L, 0.22, 4500000L, new Color4(0.906f, 0.812f, 0.49f), null, 31000, 120000, 90833L, 0L, 0L)
		};
		long total = 0L;
		foreach (DpsCanvas.PlayerRow item in rows)
		{
			total += item.Damage;
		}
		MobTarget target = new MobTarget
		{
			Name = "글래스베인",
			EntityId = 99999,
			CurrentHp = 18600000L,
			MaxHp = 26330000L,
			IsBoss = true
		};
		DpsDetailForm detailForm = null;
		canvas.PlayerRowClicked += delegate(DpsCanvas.PlayerRow row)
		{
			if (detailForm == null || detailForm.IsDisposed)
			{
				detailForm = new DpsDetailForm();
				detailForm.Show(form);
			}
			detailForm.SetData(row);
			detailForm.BringToFront();
		};
		form.Shown += delegate
		{
			canvas.SetData(rows, total, "1:24", target);
		};
		Application.Run(form);
	}

	private static (string? Dir, bool Realtime, double Speed, bool Demo, bool Admin) ParseArgs(string[] args)
	{
		string? item = null;
		bool item2 = true;
		double item3 = 1.0;
		bool item4 = false;
		bool item5 = false;
		for (int i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
			case "--replay":
				item = args[++i];
				break;
			case "--speed":
				item3 = double.Parse(args[++i]);
				break;
			case "--fast":
				item2 = false;
				break;
			case "--demo":
				item4 = true;
				break;
			case "--admin":
				item5 = true;
				break;
			}
		}
		return (Dir: item, Realtime: item2, Speed: item3, Demo: item4, Admin: item5);
	}

	private static void ExtractAdminTool()
	{
		try
		{
			var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
			Directory.CreateDirectory(dataDir);
			
			var htmlPath = Path.Combine(dataDir, "LogAnalyzer.html");
			File.WriteAllText(htmlPath, Dps.Protocol.AdminToolTemplate.HtmlContent);
			
			var logsDir = Path.Combine(dataDir, "combat_logs");
			Directory.CreateDirectory(logsDir);
			
			Console.WriteLine($"[AdminMode] Extracted LogAnalyzer.html and created combat_logs folder at: {dataDir}");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[AdminMode] Failed to extract admin tool: {ex.Message}");
		}
	}
}
