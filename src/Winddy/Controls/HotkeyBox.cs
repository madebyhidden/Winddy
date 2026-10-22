using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Winddy.Services;

namespace Winddy.Controls;

/// <summary>Shortcut recorder: focus it and press a key combination. Backspace/Delete alone clears it, Esc cancels.</summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).ShowCurrent()));

    /// <summary>True for the global shortcut, which needs at least one modifier.</summary>
    public bool RequireModifier { get; set; }

    public event EventHandler? RecordingStarted;
    public event EventHandler? RecordingEnded;

    public string Hotkey
    {
        get => (string)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        Cursor = Cursors.Hand;
        MinWidth = 180;
        Padding = new Thickness(6, 3, 6, 3);
        InputMethod.SetIsInputMethodEnabled(this, false);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = Loc.T("RecordHint");
        RecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ShowCurrent();
        RecordingEnded?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = Services.Hotkey.RealKey(e);
        var mods = Keyboard.Modifiers;

        if (key == Key.Tab && mods == ModifierKeys.None)
        {
            e.Handled = false;
            return;
        }
        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            MoveFocusAway();
            return;
        }
        if ((key is Key.Back or Key.Delete) && mods == ModifierKeys.None)
        {
            Hotkey = "";
            MoveFocusAway();
            return;
        }
        if (Services.Hotkey.IsModifierKey(key)) return;
        if (RequireModifier && (mods == ModifierKeys.None || mods == ModifierKeys.Shift)) return;

        Hotkey = new Hotkey(mods, key).ToString();
        MoveFocusAway();
    }

    private void MoveFocusAway()
    {
        var scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
        ShowCurrent();
    }

    private void ShowCurrent()
    {
        if (IsKeyboardFocused) return;
        Text = Services.Hotkey.Parse(Hotkey).Display;
    }
}
