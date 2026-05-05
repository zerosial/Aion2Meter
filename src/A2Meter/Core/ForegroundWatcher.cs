using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace A2Meter.Core;

/// Polls the foreground window to detect when Aion 2 is active.
/// When the target process is foreground → IsActive=true.
/// When A2Meter itself is foreground → preserves last state (don't hide overlay
/// just because the user clicked our settings window).
internal sealed class ForegroundWatcher : IDisposable
{
    private readonly string _processName;
    private readonly int _selfPid;
    private System.Windows.Forms.Timer? _timer;
    private bool _lastActive;

    public bool IsActive => _lastActive;
    public event Action<bool>? ActiveChanged;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public ForegroundWatcher(string processName)
    {
        _processName = processName.ToLower();
        _selfPid = Process.GetCurrentProcess().Id;
    }

    public void Start()
    {
        _timer = new System.Windows.Forms.Timer { Interval = 500 };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private void Check()
    {
        bool active = ReadForegroundActive();
        if (active != _lastActive)
        {
            _lastActive = active;
            ActiveChanged?.Invoke(active);
        }
    }

    private bool ReadForegroundActive()
    {
        try
        {
            // If Aion 2 process doesn't exist at all, always show overlay.
            if (!IsTargetProcessRunning())
                return true;

            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return _lastActive;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return _lastActive;

            // If A2Meter itself is foreground, preserve current state.
            if (pid == (uint)_selfPid)
                return _lastActive;

            var proc = Process.GetProcessById((int)pid);
            var name = proc.ProcessName;
            return name.StartsWith(_processName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return _lastActive;
        }
    }

    private bool IsTargetProcessRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName(_processName);
            bool running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            return running;
        }
        catch { return false; }
    }

    public void Dispose() => Stop();
}
