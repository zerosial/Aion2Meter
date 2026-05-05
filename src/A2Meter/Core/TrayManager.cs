using System;
using System.Drawing;
using System.Windows.Forms;
using A2Meter.Forms;

namespace A2Meter.Core;

/// System tray icon + context menu.
/// Mirrors the original A2Viewer tray (열기 / 아이온2 활성화 시에만 / UI 위치 초기화 / 종료).
internal sealed class TrayManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly OverlayForm _form;

    /// Caller persists the toggle. Tray reads/writes via the supplied delegates.
    public TrayManager(
        OverlayForm form,
        Func<bool> getOverlayOnlyWhenAion,
        Action<bool> setOverlayOnlyWhenAion)
    {
        _form = form;

        _icon = new NotifyIcon
        {
            Text = "A2Meter",
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu(getOverlayOnlyWhenAion, setOverlayOnlyWhenAion),
        };
        _icon.Click += (_, _) => _form.ToggleVisibility();
    }

    private ToolStripMenuItem? _hideItem;

    private ContextMenuStrip BuildMenu(Func<bool> getFlag, Action<bool> setFlag)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => _form.ShowOverlay());
        _hideItem = new ToolStripMenuItem("숨기기 (Alt+H)", null, (_, _) => _form.ToggleVisibility());
        menu.Items.Add(_hideItem);
        menu.Items.Add("잠금 해제", null, (_, _) => _form.Unlock());
        menu.Items.Add(new ToolStripSeparator());

        var aionOnly = new ToolStripMenuItem("아이온2 활성화 시에만 오버레이 표시")
        {
            CheckOnClick = true,
            Checked = getFlag(),
        };
        aionOnly.CheckedChanged += (s, _) =>
        {
            var item = (ToolStripMenuItem)s!;
            setFlag(item.Checked);
            _form.SetOverlayOnlyWhenAion(item.Checked);
        };
        menu.Items.Add(aionOnly);

        menu.Items.Add("UI 위치 초기화", null, (_, _) =>
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 800, 600);
            _form.Location = new Point(area.X + 100, area.Y + 100);
            _form.Size = new Size(400, 300);
            _form.ShowOverlay();
        });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => _form.RequestAppClose());

        menu.Opening += (_, _) =>
        {
            if (_hideItem != null)
                _hideItem.Text = _form.Visible ? "숨기기 (Alt+H)" : "보이기 (Alt+H)";
        };

        return menu;
    }

    public void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 5000)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.BalloonTipIcon = icon;
            _icon.ShowBalloonTip(timeoutMs);
        }
        catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
