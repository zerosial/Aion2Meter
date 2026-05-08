using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;
using A2Meter.Forms;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            string envFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
            EnvLoader.Load(envFile);
        }
        catch { /* best effort */ }

        var parsed = ParseArgs(args);

        if (parsed.Demo)
        {
            RunDemo();
            return;
        }

        var (replayDir, replayRealtime, replaySpeed, _) = parsed;

        if (replayDir is null)
        {
            _mutex = new Mutex(true, "A2Meter.SingleInstance.Mutex", out bool createdNew);
            if (!createdNew) return;
        }

        // ── WinForms + D2D mode (default) ──
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var settings = AppSettings.Instance;

        using var overlay = new OverlayForm();

        if (replayDir is not null)
        {
            overlay.PacketSourceOverride = new PcapReplaySource(replayDir, realtime: replayRealtime, speed: replaySpeed);
            overlay.Text = $"A2Meter [replay: {System.IO.Path.GetFileName(replayDir)}]";
            overlay.Tag  = System.IO.Path.GetFileName(replayDir);
        }

        overlay.HandleCreated += (_, _) =>
        {
            var hk = new HotkeyManager(overlay);
            overlay.Hotkeys = hk;
            hk.RegisterFromSettings(settings.Shortcuts);

            // Auto-update check — show toast if available.
            _ = Task.Run(async () =>
            {
                var result = await AutoUpdater.CheckAsync(msg => Console.Error.WriteLine(msg));
                if (result.HasValue)
                {
                    var (ver, url, notes) = result.Value;
                    overlay.Invoke(() =>
                    {
                        var toast = new Forms.UpdateToastForm(overlay, ver, url, notes);
                        toast.Show();
                    });
                }
            });
        };

        using var tray = new TrayManager(
            overlay,
            getOverlayOnlyWhenAion: () => settings.OverlayOnlyWhenAion,
            setOverlayOnlyWhenAion: v =>
            {
                settings.OverlayOnlyWhenAion = v;
                settings.SaveDebounced();
            });

        overlay.AppCloseRequested += (_, _) =>
        {
            settings.Save();
            Application.Exit();
        };

        Application.Run(overlay);
    }

    private static void RunDemo()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var canvas = new DpsCanvas { Dock = DockStyle.Fill };

        var form = new Form
        {
            Text = "A2Meter [demo]",
            FormBorderStyle = FormBorderStyle.Sizable,
            StartPosition = FormStartPosition.CenterScreen,
            Size = new System.Drawing.Size(460, 500),
            BackColor = System.Drawing.Color.FromArgb(8, 11, 20),
        };
        form.Controls.Add(canvas);

        // Dummy data: boss 1, party 4 (수호성, 살성, 마도성, 치유성)
        // Colors match original A2Viewer palette.
        var skills = new List<DpsCanvas.SkillBar>
        {
            new("철벽 방어",   3_200_000, 142, 0.35, 0.38),
            new("심판의 일격", 2_100_000,  98, 0.42, 0.25),
            new("도발 강타",   1_500_000,  76, 0.28, 0.18),
            new("방패 돌진",     980_000,  54, 0.31, 0.12),
            new("수호의 맹세",   450_000,  32, 0.15, 0.05),
        };

        var rows = new List<DpsCanvas.PlayerRow>
        {
            new("수호성",   "수호성", 8_450_000, 1.00, 352_083, 0.34, 0,
                new D2DColor(0.490f, 0.627f, 0.976f, 1f), skills, 42000, 410_000, 352_083, 120_000),
            new("살성",    "살성",   7_820_000, 0.93, 325_833, 0.48, 0,
                new D2DColor(0.643f, 0.906f, 0.608f, 1f), null, 38500, 398_000, 325_833, 0),
            new("마도성",  "마도성", 6_950_000, 0.82, 289_583, 0.41, 0,
                new D2DColor(0.718f, 0.549f, 0.949f, 1f), null, 35200, 372_000, 289_583, 1_200_000),
            new("치유성",  "치유성", 2_180_000, 0.26, 90_833,  0.22, 4_500_000,
                new D2DColor(0.906f, 0.812f, 0.490f, 1f), null, 31000, 120_000, 90_833, 0),
        };

        long total = 0;
        foreach (var r in rows) total += r.Damage;

        var target = new MobTarget
        {
            Name = "글래스베인",
            EntityId = 99999,
            CurrentHp = 18_600_000,
            MaxHp = 26_330_000,
            IsBoss = true,
        };

        DpsDetailForm? detailForm = null;
        canvas.PlayerRowClicked += row =>
        {
            if (detailForm == null || detailForm.IsDisposed)
            {
                detailForm = new DpsDetailForm();
                detailForm.Show(form);
            }
            detailForm.SetData(row);
            detailForm.BringToFront();
        };

        form.Shown += (_, _) =>
        {
            canvas.SetData(rows, total, "1:24", target);
        };

        Application.Run(form);
    }

    /// CLI:
    ///   A2Meter                                    # live capture
    ///   A2Meter --replay <session-dir>             # offline replay, realtime
    ///   A2Meter --replay <dir> --speed 4           # replay 4x faster
    ///   A2Meter --replay <dir> --fast              # replay as fast as possible
    ///   A2Meter --demo                              # dummy data preview
    private static (string? Dir, bool Realtime, double Speed, bool Demo) ParseArgs(string[] args)
    {
        string? dir = null;
        bool realtime = true;
        double speed = 1.0;
        bool demo = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--replay": dir = args[++i]; break;
                case "--speed":  speed = double.Parse(args[++i]); break;
                case "--fast":   realtime = false; break;
                case "--demo":   demo = true; break;
            }
        }
        return (dir, realtime, speed, demo);
    }
}
