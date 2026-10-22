using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Winddy.Models;

namespace Winddy.Services;

/// <summary>
/// Several selected items queued for pasting one after another: every Ctrl+V the user presses
/// in any app puts the next item on the clipboard, until the stack is empty.
/// </summary>
public sealed class PasteStack : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    private readonly ClipboardService _clipboard;
    private readonly Dispatcher _dispatcher;
    private readonly HookProc _proc;
    private IntPtr _hook;
    private readonly Queue<HistoryItem> _items = new();
    private bool _removeFormatting;
    private bool _pasteDown;

    public event Action? Changed;

    public PasteStack(ClipboardService clipboard, Dispatcher dispatcher)
    {
        _clipboard = clipboard;
        _dispatcher = dispatcher;
        _proc = HookCallback; // keep the delegate alive while hooked
    }

    public bool IsActive => _items.Count > 0;
    public int Remaining => _items.Count;
    public IEnumerable<HistoryItem> Items => _items;

    /// <summary>Starts a stack: the first item goes on the clipboard right away.</summary>
    public void Start(IReadOnlyList<HistoryItem> items, bool removeFormatting)
    {
        Stop();
        if (items.Count == 0) return;
        foreach (var item in items) _items.Enqueue(item);
        _removeFormatting = removeFormatting;
        _clipboard.Copy(_items.Peek(), removeFormatting);
        using (var module = Process.GetCurrentProcess().MainModule)
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        Changed?.Invoke();
    }

    /// <summary>Called after the current item was pasted: moves on to the next one.</summary>
    public void Advance()
    {
        if (_items.Count == 0) return;
        _items.Dequeue();
        if (_items.Count == 0)
        {
            Stop();
            return;
        }
        _clipboard.Copy(_items.Peek(), _removeFormatting);
        Changed?.Invoke();
    }

    public void Stop()
    {
        var wasActive = _items.Count > 0 || _hook != IntPtr.Zero;
        _items.Clear();
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        if (wasActive) Changed?.Invoke();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == Native.VK_V && (info.flags & LLKHF_INJECTED) == 0)
            {
                var up = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;
                if (!up && Native.IsKeyDown(Native.VK_CONTROL))
                {
                    _pasteDown = true;
                }
                else if (up && _pasteDown)
                {
                    // Ctrl+V released: the target app has read the clipboard by now.
                    _pasteDown = false;
                    _dispatcher.BeginInvoke(Advance, DispatcherPriority.Background);
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
