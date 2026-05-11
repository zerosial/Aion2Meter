using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Api;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;
using A2Meter.Dps.Protocol;

namespace A2Meter.Forms;

internal sealed class OverlayForm : Form
{
	private struct TRACKMOUSEEVENT
	{
		public int cbSize;

		public int dwFlags;

		public nint hwndTrack;

		public int dwHoverTime;
	}

	private struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private const int HeaderHeight = 36;

	private const int ResizeMargin = 10;

	private OverlayRenderer? _renderer;

	private IPacketSource? _source;

	private readonly DpsMeter _meter = new DpsMeter();

	private readonly PartyTracker _party = new PartyTracker();

	private DpsPipeline? _pipeline;

	private ProtocolPipeline? _protocol;

	private ForegroundWatcher? _fgWatcher;

	private bool _locked;

	private bool _anonymous;

	private bool _appCloseRequested;

	private bool _loaded;

	private bool _sliderDragging;

	private const int WM_MOUSEMOVE = 512;

	private const int WM_LBUTTONDOWN = 513;

	private const int WM_LBUTTONUP = 514;

	private const int WM_MOUSELEAVE = 675;

	private const int TME_LEAVE = 2;

	private bool _trackingMouse;

	private LockButtonForm? _lockBtn;

	private const int SnapDistance = 8;

	public IPacketSource? PacketSourceOverride { get; set; }

	public HotkeyManager? Hotkeys { get; set; }

	public SecondaryWindows Windows { get; }

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 134742152;
			return createParams;
		}
	}

	protected override bool ShowWithoutActivation => true;

	public event EventHandler? AppCloseRequested;

	[DllImport("user32.dll")]
	private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT evt);

	[DllImport("user32.dll")]
	private static extern nint SetCapture(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool ReleaseCapture();

	private void PersistWindowState()
	{
		if (_loaded && base.WindowState == FormWindowState.Normal)
		{
			AppSettings instance = AppSettings.Instance;
			instance.WindowState.X = base.Location.X;
			instance.WindowState.Y = base.Location.Y;
			instance.WindowState.Width = base.Size.Width;
			instance.WindowState.Height = base.Size.Height;
			instance.SaveDebounced();
		}
	}

	protected override void OnMove(EventArgs e)
	{
		base.OnMove(e);
		PersistWindowState();
		RequestRender();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		PersistWindowState();
		RequestRender();
	}

	protected override void OnResizeEnd(EventArgs e)
	{
		base.OnResizeEnd(e);
		PersistWindowState();
		RequestRender();
	}

	public OverlayForm()
	{
		Text = "A2Meter";
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.StartPosition = FormStartPosition.Manual;
		MinimumSize = new Size(100, 100);
		WindowState windowState = AppSettings.Instance.WindowState;
		base.Location = new Point(windowState.X, windowState.Y);
		base.Size = new Size((windowState.Width >= MinimumSize.Width) ? windowState.Width : 460, (windowState.Height >= MinimumSize.Height) ? windowState.Height : 500);
		Windows = new SecondaryWindows(this);
		base.Load += delegate
		{
			_loaded = true;
			InitOverlay();
		};
	}

	private void InitOverlay()
	{
		_renderer = new OverlayRenderer();
		_renderer.Init();
		if (_source == null)
		{
			_source = PacketSourceOverride ?? new PacketSniffer();
		}
		_protocol = new ProtocolPipeline(_source, null, delegate(string msg)
		{
			Console.Error.WriteLine(msg);
		});
		_pipeline = new DpsPipeline(_source, _meter, _party);
		_pipeline.DataPushed += OnDataPushed;
		_pipeline.CombatStarted += OnCombatStarted;
		try
		{
			_pipeline.Start();
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("[overlay] packet source failed to start: " + ex.Message);
		}
		_fgWatcher = new ForegroundWatcher("aion2");
		_fgWatcher.ActiveChanged += OnAionActiveChanged;
		if (AppSettings.Instance.OverlayOnlyWhenAion)
		{
			_fgWatcher.Start();
		}
		RequestRender();
	}

	private void OnDataPushed(IReadOnlyList<DpsCanvas.PlayerRow> rows, long total, string timer, MobTarget? target, DpsCanvas.SessionSummary? summary)
	{
		if (_renderer == null || !base.IsHandleCreated || base.IsDisposed)
		{
			return;
		}
		try
		{
			BeginInvoke(delegate
			{
				if (_renderer != null)
				{
					_renderer.CountdownSec = _pipeline?.CountdownSeconds ?? 0;
					_renderer.CountdownExpired = _pipeline?.CountdownExpired ?? false;
					_renderer.PingMs = _pipeline?.Ping.CurrentPingMs ?? 0;
					_renderer.SetData(rows, total, timer, target, summary);
					_renderer.SetPartyData(BuildPartyRows());
					RequestRender();
				}
			});
		}
		catch
		{
		}
	}

	private void OnCombatStarted()
	{
		if (_renderer == null || !base.IsHandleCreated || base.IsDisposed)
		{
			return;
		}
		try
		{
			BeginInvoke(delegate
			{
				if (_renderer != null && _renderer.ActiveTab != OverlayRenderer.TabId.Dps)
				{
					_renderer.ActiveTab = OverlayRenderer.TabId.Dps;
					RequestRender();
				}
			});
		}
		catch
		{
		}
	}

	private List<OverlayRenderer.PartyRow> BuildPartyRows()
	{
		List<OverlayRenderer.PartyRow> list = new List<OverlayRenderer.PartyRow>();
		PartyMember[] array;
		try
		{
			array = _party.Members.Values.ToArray();
		}
		catch
		{
			return list;
		}
		PartyMember[] array2 = array;
		foreach (PartyMember partyMember in array2)
		{
			if (string.IsNullOrEmpty(partyMember.Nickname) || (!partyMember.IsSelf && !partyMember.IsPartyMember && !partyMember.IsLookup))
			{
				continue;
			}
			int combatPower = partyMember.CombatPower;
			int num = 0;
			int serverId = partyMember.ServerId;
			string text = partyMember.ServerName;
			if (string.IsNullOrEmpty(text) && serverId > 0)
			{
				text = ServerMap.GetName(serverId);
			}
			CharacterSkillData characterSkillData = SkillLevelCache.Instance.Get(partyMember.Nickname, serverId);
			if (characterSkillData != null)
			{
				if (combatPower == 0 && characterSkillData.CombatPower > 0)
				{
					combatPower = characterSkillData.CombatPower;
				}
				if (num == 0 && characterSkillData.CombatScore > 0)
				{
					num = characterSkillData.CombatScore;
				}
			}
			list.Add(new OverlayRenderer.PartyRow((!string.IsNullOrEmpty(text) && !partyMember.Nickname.Contains('[')) ? (partyMember.Nickname + "[" + text + "]") : partyMember.Nickname, JobMapping.GameToJobName(partyMember.JobCode), combatPower, num, serverId, text, partyMember.IsSelf, partyMember.Level));
		}
		list.Sort((OverlayRenderer.PartyRow a, OverlayRenderer.PartyRow b) => (a.IsSelf != b.IsSelf) ? ((!a.IsSelf) ? 1 : (-1)) : b.CombatPower.CompareTo(a.CombatPower));
		return list;
	}

	private void RequestRender()
	{
		if (_renderer != null && base.IsHandleCreated && !base.IsDisposed && base.Width > 0 && base.Height > 0)
		{
			_renderer.RenderFrame(base.Width, base.Height);
			_renderer.PresentToLayeredWindow(base.Handle, base.Left, base.Top, base.Width, base.Height);
		}
	}

	private void OnAionActiveChanged(bool active)
	{
		if (AppSettings.Instance.OverlayOnlyWhenAion)
		{
			if (active)
			{
				ShowOverlay();
			}
			else
			{
				HideOverlay();
			}
		}
	}

	public void SetOverlayOnlyWhenAion(bool enabled)
	{
		if (enabled)
		{
			_fgWatcher?.Start();
			if (_fgWatcher != null && !_fgWatcher.IsActive)
			{
				HideOverlay();
			}
		}
		else
		{
			_fgWatcher?.Stop();
			ShowOverlay();
		}
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		_fgWatcher?.Dispose();
		_lockBtn?.Close();
		_pipeline?.Dispose();
		_protocol?.Dispose();
		_renderer?.Dispose();
		base.OnFormClosed(e);
	}

	private void OpenHistory()
	{
		if (_pipeline != null)
		{
			CombatHistoryForm combatHistoryForm = Windows.Open<CombatHistoryForm>();
			combatHistoryForm.FormClosed += delegate
			{
				_pipeline.ExitHistoryView();
			};
			combatHistoryForm.SetData(_pipeline.History, delegate(CombatRecord record)
			{
				_pipeline.EnterHistoryView();
				IReadOnlyList<DpsCanvas.PlayerRow> readOnlyList = _pipeline.MapSnapshotForCanvas(record.Snapshot);
				DpsCanvas.SessionSummary summary = new DpsCanvas.SessionSummary(record.DurationSec, record.TotalDamage, record.AverageDps, record.PeakDps, (readOnlyList.Count > 0) ? readOnlyList[0].Name : "", (readOnlyList.Count > 0) ? readOnlyList[0].Damage : 0, record.BossName);
				string timer = $"{(int)record.DurationSec / 60}:{(int)record.DurationSec % 60:00}";
				_renderer?.SetData(readOnlyList, record.TotalDamage, timer, record.Snapshot.Target, summary);
				RequestRender();
			});
		}
	}

	private void OpenSettings()
	{
		Windows.Open<SettingsPanelForm>().SettingsChanged += delegate
		{
			_renderer?.ApplySettings();
			RequestRender();
		};
	}

	private void OnOpacitySlider(int value)
	{
		AppSettings.Instance.Opacity = value;
		AppSettings.Instance.SaveDebounced();
		RequestRender();
	}

	private void SetLocked(bool locked)
	{
		_locked = locked;
		_renderer?.SetLocked(locked);
		int windowLong = Win32Native.GetWindowLong(base.Handle, -20);
		if (!locked)
		{
			OverlayRenderer? renderer = _renderer;
			if (renderer == null || !renderer.CompactMode)
			{
				windowLong &= -33;
				goto IL_004d;
			}
		}
		windowLong |= 0x20;
		goto IL_004d;
		IL_004d:
		Win32Native.SetWindowLong(base.Handle, -20, windowLong);
		if (locked)
		{
			if (_lockBtn == null)
			{
				_lockBtn = new LockButtonForm(this);
			}
			_lockBtn.PlaceNear(this);
			_lockBtn.Show();
		}
		else
		{
			_lockBtn?.Hide();
		}
		RequestRender();
	}

	public void Unlock()
	{
		SetLocked(locked: false);
		_lockBtn?.Hide();
	}

	public void ShowOverlay()
	{
		if (!base.Visible)
		{
			Show();
		}
		if (base.WindowState == FormWindowState.Minimized)
		{
			base.WindowState = FormWindowState.Normal;
		}
		BringToFront();
		RequestRender();
	}

	public void HideOverlay()
	{
		Hide();
	}

	public void ToggleVisibility()
	{
		if (base.Visible)
		{
			HideOverlay();
		}
		else
		{
			ShowOverlay();
		}
	}

	public void ToggleCompact()
	{
		if (_renderer != null)
		{
			bool flag = !_renderer.CompactMode;
			_renderer.CompactMode = flag;
			int num = Win32Native.GetWindowLong(base.Handle, -20);
			if (flag)
			{
				num |= 0x20;
			}
			else if (!_locked)
			{
				num &= -33;
			}
			Win32Native.SetWindowLong(base.Handle, -20, num);
			RequestRender();
		}
	}

	public void TriggerClearShortcut()
	{
		_pipeline?.Reset();
	}

	public void TriggerSwitchTab()
	{
		if (_renderer != null)
		{
			_renderer.ActiveTab = ((_renderer.ActiveTab == OverlayRenderer.TabId.Dps) ? OverlayRenderer.TabId.Party : OverlayRenderer.TabId.Dps);
			RequestRender();
		}
	}

	public void TriggerRestart()
	{
		string processPath = Environment.ProcessPath;
		if (!string.IsNullOrEmpty(processPath))
		{
			Process.Start(processPath);
		}
		Environment.Exit(0);
	}

	public void TriggerAnonymousToggle()
	{
		_anonymous = !_anonymous;
		_renderer?.SetAnonymous(_anonymous);
		RequestRender();
	}

	private void OnCountdownClicked()
	{
		if (_pipeline != null)
		{
			_pipeline.CycleCountdown();
			if (_renderer != null)
			{
				_renderer.CountdownSec = _pipeline.CountdownSeconds;
				_renderer.CountdownExpired = _pipeline.CountdownExpired;
			}
			RequestRender();
		}
	}

	public void RequestAppClose()
	{
		_appCloseRequested = true;
		Windows.CloseAll();
		this.AppCloseRequested?.Invoke(this, EventArgs.Empty);
		Close();
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 786)
		{
			Hotkeys?.ProcessHotkey(((IntPtr)m.WParam).ToInt32());
			return;
		}
		if (m.Msg == 534)
		{
			SnapEdges(m.LParam);
			m.Result = IntPtr.Zero;
		}
		if (m.Msg == 132 && !_locked)
		{
			int num = (int)m.LParam;
			Point pt = PointToClient(new Point((short)(num & 0xFFFF), (short)((num >> 16) & 0xFFFF)));
			int num2 = HitTestEdges(pt);
			if (num2 != 1)
			{
				m.Result = num2;
				return;
			}
			if (_renderer != null && _renderer.IsDragArea(pt))
			{
				m.Result = 2;
				return;
			}
		}
		if (m.Msg == 512 && _renderer != null)
		{
			if (!_trackingMouse)
			{
				TRACKMOUSEEVENT evt = new TRACKMOUSEEVENT
				{
					cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(),
					dwFlags = 2,
					hwndTrack = base.Handle
				};
				TrackMouseEvent(ref evt);
				_trackingMouse = true;
			}
			int num3 = (int)m.LParam;
			Point pt2 = new Point((short)(num3 & 0xFFFF), (short)((num3 >> 16) & 0xFFFF));
			if (_sliderDragging)
			{
				float num4 = _renderer.SliderValueFromX(pt2.X);
				int value = 20 + (int)(num4 * 80f);
				OnOpacitySlider(Math.Clamp(value, 20, 100));
				return;
			}
			OverlayRenderer.ZoneId hoveredZone = _renderer.HitTest(pt2);
			_renderer.SetHoveredZone(hoveredZone);
			RequestRender();
		}
		if (m.Msg == 675)
		{
			_trackingMouse = false;
			_renderer?.SetHoveredZone(OverlayRenderer.ZoneId.None);
			RequestRender();
		}
		if (m.Msg == 513 && _renderer != null)
		{
			int num5 = (int)m.LParam;
			Point pt3 = new Point((short)(num5 & 0xFFFF), (short)((num5 >> 16) & 0xFFFF));
			if (_renderer.HitTest(pt3) == OverlayRenderer.ZoneId.Slider)
			{
				_sliderDragging = true;
				SetCapture(base.Handle);
				float num6 = _renderer.SliderValueFromX(pt3.X);
				int value2 = 20 + (int)(num6 * 80f);
				OnOpacitySlider(Math.Clamp(value2, 20, 100));
				return;
			}
		}
		if (m.Msg == 514 && _renderer != null)
		{
			if (_sliderDragging)
			{
				_sliderDragging = false;
				ReleaseCapture();
				return;
			}
			int num7 = (int)m.LParam;
			Point pt4 = new Point((short)(num7 & 0xFFFF), (short)((num7 >> 16) & 0xFFFF));
			switch (_renderer.HitTest(pt4))
			{
			case OverlayRenderer.ZoneId.Lock:
				SetLocked(!_locked);
				break;
			case OverlayRenderer.ZoneId.Anon:
				TriggerAnonymousToggle();
				break;
			case OverlayRenderer.ZoneId.History:
				OpenHistory();
				break;
			case OverlayRenderer.ZoneId.Settings:
				OpenSettings();
				break;
			case OverlayRenderer.ZoneId.Close:
				RequestAppClose();
				break;
			case OverlayRenderer.ZoneId.Countdown:
				OnCountdownClicked();
				break;
			case OverlayRenderer.ZoneId.CpToggle:
			{
				AppSettings instance2 = AppSettings.Instance;
				instance2.ShowCombatPower = !instance2.ShowCombatPower;
				instance2.SaveDebounced();
				RequestRender();
				break;
			}
			case OverlayRenderer.ZoneId.ScoreToggle:
			{
				AppSettings instance = AppSettings.Instance;
				instance.ShowCombatScore = !instance.ShowCombatScore;
				instance.SaveDebounced();
				RequestRender();
				break;
			}
			case OverlayRenderer.ZoneId.TabDps:
				if (_renderer.ActiveTab != OverlayRenderer.TabId.Dps)
				{
					_renderer.ActiveTab = OverlayRenderer.TabId.Dps;
					RequestRender();
				}
				break;
			case OverlayRenderer.ZoneId.TabParty:
				if (_renderer.ActiveTab != OverlayRenderer.TabId.Party)
				{
					_renderer.ActiveTab = OverlayRenderer.TabId.Party;
					RequestRender();
				}
				break;
			case OverlayRenderer.ZoneId.None:
			{
				int num8 = _renderer.RowHitTest(pt4.Y);
				if (num8 >= 0)
				{
					OnPlayerRowClicked(num8);
				}
				break;
			}
			}
		}
		base.WndProc(ref m);
	}

	private void OnPlayerRowClicked(int rowIdx)
	{
		IReadOnlyList<DpsCanvas.PlayerRow> readOnlyList = _renderer?.GetRows();
		if (readOnlyList != null && rowIdx >= 0 && rowIdx < readOnlyList.Count)
		{
			Windows.Open<DpsDetailForm>().SetData(readOnlyList[rowIdx]);
		}
	}

	private int HitTestEdges(Point pt)
	{
		int width = base.ClientSize.Width;
		int height = base.ClientSize.Height;
		bool flag = pt.X < 10;
		bool flag2 = pt.X >= width - 10;
		bool flag3 = pt.Y < 10;
		bool flag4 = pt.Y >= height - 10;
		if (flag3 && flag)
		{
			return 13;
		}
		if (flag3 && flag2)
		{
			return 14;
		}
		if (flag4 && flag)
		{
			return 16;
		}
		if (flag4 && flag2)
		{
			return 17;
		}
		if (flag)
		{
			return 10;
		}
		if (flag2)
		{
			return 11;
		}
		if (flag3)
		{
			return 12;
		}
		if (flag4)
		{
			return 15;
		}
		return 1;
	}

	private unsafe static void SnapEdges(nint lParam)
	{
		ref RECT reference = ref *(RECT*)lParam;
		int num = reference.Right - reference.Left;
		int num2 = reference.Bottom - reference.Top;
		Rectangle workingArea = Screen.FromRectangle(new Rectangle(reference.Left, reference.Top, num, num2)).WorkingArea;
		if (Math.Abs(reference.Left - workingArea.Left) < 8)
		{
			reference.Left = workingArea.Left;
			reference.Right = reference.Left + num;
		}
		else if (Math.Abs(reference.Right - workingArea.Right) < 8)
		{
			reference.Right = workingArea.Right;
			reference.Left = reference.Right - num;
		}
		if (Math.Abs(reference.Top - workingArea.Top) < 8)
		{
			reference.Top = workingArea.Top;
			reference.Bottom = reference.Top + num2;
		}
		else if (Math.Abs(reference.Bottom - workingArea.Bottom) < 8)
		{
			reference.Bottom = workingArea.Bottom;
			reference.Top = reference.Bottom - num2;
		}
	}
}
