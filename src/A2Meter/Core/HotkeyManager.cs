using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Forms;

namespace A2Meter.Core;

/// Global hotkeys via WH_KEYBOARD_LL — intercepts all key events at the lowest level,
/// before any other application. Registered shortcuts are consumed (not forwarded).
internal sealed class HotkeyManager : IDisposable
{
    public const int WM_HOTKEY = 0x0312; // kept for OverlayForm.WndProc compat (unused now)

    private readonly OverlayForm _form;
    private readonly List<HotkeyBinding> _bindings = new();
    private bool _suspended;

    private IntPtr _hookId;
    private readonly LowLevelKeyboardProc _hookProc; // prevent GC

    // ── Win32 ──
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private sealed record HotkeyBinding(uint Modifiers, uint Vk, Action Callback);

    private const uint MOD_ALT   = 0x0001;
    private const uint MOD_CTRL  = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private const int VK_LSHIFT   = 0xA0;
    private const int VK_RSHIFT   = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU    = 0xA4;
    private const int VK_RMENU    = 0xA5;

    public HotkeyManager(OverlayForm form)
    {
        _form = form;
        _hookProc = HookCallback;
        InstallHook();
    }

    public void RegisterFromSettings(ShortcutSettings shortcuts)
    {
        _bindings.Clear();
        TryAdd(shortcuts.Reset,     () => _form.TriggerClearShortcut());
        TryAdd(shortcuts.Restart,   () => _form.TriggerRestart());
        TryAdd(shortcuts.Anonymous, () => _form.TriggerAnonymousToggle());
        TryAdd(shortcuts.Compact,   () => _form.ToggleCompact());
        TryAdd(shortcuts.Hide,      () => _form.ToggleVisibility());
    }

    public void Suspend() => _suspended = true;

    public void Resume(ShortcutSettings shortcuts)
    {
        _suspended = false;
        RegisterFromSettings(shortcuts);
    }

    public void UnregisterAll() => _bindings.Clear();

    /// Legacy: no longer used (hook-based now), but kept so callers don't break.
    public void ProcessHotkey(int id) { }

    public void Dispose()
    {
        _bindings.Clear();
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    // ── Hook install ──

    private void InstallHook()
    {
        using var proc = Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(mod.ModuleName), 0);
    }

    // ── Hook callback ──

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_suspended && _bindings.Count > 0)
        {
            int msg = (int)wParam;
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                uint currentMods = GetCurrentModifiers();

                foreach (var b in _bindings)
                {
                    if (kbd.vkCode == b.Vk && currentMods == b.Modifiers)
                    {
                        // Invoke on UI thread, suppress keystroke.
                        _form.BeginInvoke(b.Callback);
                        return (IntPtr)1; // consumed — don't pass to next hook/app
                    }
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static uint GetCurrentModifiers()
    {
        uint mods = 0;
        if (IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL)) mods |= MOD_CTRL;
        if (IsKeyDown(VK_LMENU)    || IsKeyDown(VK_RMENU))    mods |= MOD_ALT;
        if (IsKeyDown(VK_LSHIFT)   || IsKeyDown(VK_RSHIFT))   mods |= MOD_SHIFT;
        return mods;
    }

    private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // ── Binding helpers ──

    private void TryAdd(string? accelerator, Action action)
    {
        if (string.IsNullOrWhiteSpace(accelerator)) return;
        if (!ParseAccelerator(accelerator, out var modifiers, out var vk)) return;
        _bindings.Add(new HotkeyBinding(modifiers, vk, action));
    }

    private static bool ParseAccelerator(string accel, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        foreach (var raw in accel.Split('+'))
        {
            var token = raw.Trim();
            var lower = token.ToLowerInvariant();
            switch (lower)
            {
                case "alt":     modifiers |= MOD_ALT;   continue;
                case "ctrl":
                case "control": modifiers |= MOD_CTRL;  continue;
                case "shift":   modifiers |= MOD_SHIFT; continue;
                case "`": case "~": case "backquote":
                    vk = 0xC0; continue;  // VK_OEM_3
                case "-": case "minus":
                    vk = 0xBD; continue;  // VK_OEM_MINUS
                case "/": case "slash":
                    vk = 0xBF; continue;  // VK_OEM_2
            }
            if (token.Length == 1 && char.IsDigit(token[0])) { vk = token[0]; continue; }
            if (token.Length == 1 && char.IsLetter(token[0])) { vk = (uint)char.ToUpperInvariant(token[0]); continue; }
            if (Enum.TryParse<Keys>(token, ignoreCase: true, out var k)) { vk = (uint)k; continue; }
        }
        return vk != 0;
    }
}
