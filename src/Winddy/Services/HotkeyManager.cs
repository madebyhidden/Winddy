using System;
using System.Windows.Interop;

namespace Winddy.Services;

/// <summary>Registers the system-wide shortcut that opens the popup.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x5744; // "WD"
    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public Hotkey Current { get; private set; } = Hotkey.None;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("WinddyHotkey") { ParentWindow = Native.HWND_MESSAGE, WindowStyle = 0 };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers the hotkey; returns false when another application already owns it.</summary>
    public bool Register(Hotkey hotkey)
    {
        Unregister();
        Current = hotkey;
        if (hotkey.IsEmpty) return true;
        // No MOD_NOREPEAT: holding the modifiers and tapping the key again cycles through items.
        _registered = Native.RegisterHotKey(_source.Handle, HotkeyId, hotkey.NativeModifiers, hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        Native.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    /// <summary>Temporarily releases the hotkey, e.g. while the user records a new one.</summary>
    public void Suspend() => Unregister();

    public void Resume() => Register(Current);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.Dispose();
    }
}
