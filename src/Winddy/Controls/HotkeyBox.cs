using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Winddy.Services;

namespace Winddy.Controls;

/// <summary>
/// Shortcut picker in the style of Windows/PowerToys: the shortcut is shown as accent keycaps.
/// Click it and press a combination; Backspace/Delete alone clears it, Esc cancels.
/// </summary>
public sealed class HotkeyBox : Button
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Render()));

    private bool _recording;

    /// <summary>True for the global shortcut, which needs at least one modifier besides Shift.</summary>
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
        SetResourceReference(StyleProperty, typeof(Button));
        MinHeight = 36;
        MinWidth = 120;
        Padding = new Thickness(6, 4, 10, 4);
        HorizontalContentAlignment = HorizontalAlignment.Left;
        InputMethod.SetIsInputMethodEnabled(this, false);
        Loaded += (_, _) => Render();
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (_recording) return;
        _recording = true;
        Focus();
        Keyboard.Focus(this);
        Render();
        RecordingStarted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        StopRecording();
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        Render();
        RecordingEnded?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = Services.Hotkey.RealKey(e);
        var mods = Keyboard.Modifiers;

        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            StopRecording();
            return;
        }
        if ((key is Key.Back or Key.Delete) && mods == ModifierKeys.None)
        {
            Hotkey = "";
            StopRecording();
            return;
        }
        if (Services.Hotkey.IsModifierKey(key)) return;
        if (RequireModifier && (mods == ModifierKeys.None || mods == ModifierKeys.Shift)) return;

        Hotkey = new Hotkey(mods, key).ToString();
        StopRecording();
    }

    private void Render()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        if (_recording)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("RecordHint"),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            BorderThickness = new Thickness(2);
            SetResourceReference(BorderBrushProperty, "SystemAccentColorPrimaryBrush");
        }
        else
        {
            ClearValue(BorderThicknessProperty);
            ClearValue(BorderBrushProperty);
            var display = Services.Hotkey.Parse(Hotkey).Display;
            if (display.Length == 0)
            {
                panel.Children.Add(new TextBlock { Text = "—", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                foreach (var part in display.Split('+'))
                    panel.Children.Add(Keycap(part));
            }
            panel.Children.Add(new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                Margin = new Thickness(10, 0, 0, 0),
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        Content = panel;
    }

    private static Border Keycap(string text)
    {
        var label = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        var cap = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(2, 0, 2, 0),
            MinWidth = 28,
            Child = label,
        };
        label.HorizontalAlignment = HorizontalAlignment.Center;
        cap.SetResourceReference(Border.BackgroundProperty, "SystemAccentColorPrimaryBrush");
        return cap;
    }
}
