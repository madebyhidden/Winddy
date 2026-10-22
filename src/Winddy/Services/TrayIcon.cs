using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Winddy.Models;
using Forms = System.Windows.Forms;

namespace Winddy.Services;

/// <summary>
/// Notification-area icon, the Windows counterpart of Maccy's menu bar icon.
/// Click opens the popup, Alt+click pauses, Alt+Shift+click ignores the next copy.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _normal;
    private readonly Icon _paused;
    private readonly ContextMenu _menu;
    private readonly MenuItem _pauseItem;
    private readonly MenuItem _stopStackItem;
    private string _recent = "";

    public TrayIcon(App app)
    {
        _app = app;
        _normal = LoadIcon("Winddy.ico");
        _paused = LoadIcon("WinddyPaused.ico");

        // A WPF menu so it gets the Windows 11 Fluent look (rounded, acrylic-like, Segoe icons).
        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        _menu.Items.Add(Item("Open", "\uE8A7", () => _app.Popup.ShowPopup(fromTray: true)));
        _menu.Items.Add(new Separator());
        _pauseItem = Item("Pause", "\uE769", () => _app.TogglePause());
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(Item("IgnoreNext", "\uE8D8", () => _app.IgnoreNextCopy()));
        _stopStackItem = Item("StopPasteStack", "\uE71A", () => _app.PasteStack.Stop());
        _stopStackItem.Visibility = Visibility.Collapsed;
        _menu.Items.Add(_stopStackItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Preferences", "\uE713", () => _app.ShowSettings()));
        _menu.Items.Add(Item("CheckForUpdatesMenu", "\uE895", () => _app.CheckForUpdatesNow()));
        _menu.Items.Add(Item("About", "\uE946", () => _app.ShowAbout()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Quit", "\uE7E8", () => _app.Quit()));
        _menu.Opened += (_, _) =>
        {
            // Without foreground activation the menu would not close when clicking elsewhere.
            if (PresentationSource.FromVisual(_menu) is HwndSource source) Native.SetForegroundWindow(source.Handle);
        };

        _icon = new Forms.NotifyIcon();
        _icon.MouseUp += OnMouseUp;
        ApplySettings();
    }

    private static MenuItem Item(string key, string glyph, Action action)
    {
        var icon = new TextBlock { Text = glyph };
        icon.SetResourceReference(FrameworkElement.StyleProperty, "Glyph");
        var item = new MenuItem { Header = Loc.T(key), Icon = icon };
        item.Click += (_, _) => action();
        return item;
    }

    private static Icon LoadIcon(string name)
    {
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
        return new Icon(info!.Stream, Forms.SystemInformation.SmallIconSize);
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Right)
        {
            _menu.IsOpen = true;
            return;
        }
        if (e.Button != Forms.MouseButtons.Left) return;
        var mods = Forms.Control.ModifierKeys;
        if (mods.HasFlag(Forms.Keys.Alt) && mods.HasFlag(Forms.Keys.Shift))
            _app.IgnoreNextCopy();
        else if (mods.HasFlag(Forms.Keys.Alt))
            _app.TogglePause();
        else
            _app.Popup.Toggle(fromTray: true);
    }

    public void ApplySettings()
    {
        var s = AppSettings.Current;
        var paused = s.IgnoreEvents;
        _icon.Icon = paused ? _paused : _normal;
        _pauseItem.Header = Loc.T(paused ? "Resume" : "Pause");
        _icon.Visible = s.ShowInTray;
        _stopStackItem.Visibility = _app.PasteStack?.IsActive == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshText();
    }

    public void UpdateTooltip(HistoryItem? recent)
    {
        _recent = recent == null ? "" : recent.HasImage ? Loc.T("Image") : HistoryItem.ShortenTitle(recent.SearchableText, false);
        RefreshText();
    }

    private void RefreshText()
    {
        var text = Loc.T("TrayTooltip");
        if (AppSettings.Current.IgnoreEvents) text += $" ({Loc.T("TrayPaused")})";
        if (_app.PasteStack?.IsActive == true) text += "\n" + Loc.T("PasteStackLeft", _app.PasteStack.Remaining);
        if (AppSettings.Current.ShowRecentCopyInTray && _recent.Length > 0) text += "\n" + _recent;
        // NotifyIcon.Text is limited to 127 characters.
        _icon.Text = text.Length > 127 ? text[..126] + "…" : text;
    }

    public void OpenMenuForSnapshot()
    {
        _menu.Placement = PlacementMode.AbsolutePoint;
        _menu.HorizontalOffset = 200;
        _menu.VerticalOffset = 120;
        _menu.IsOpen = true;
    }

    public void ShowNotification(string message)
    {
        if (!_icon.Visible) return;
        _icon.ShowBalloonTip(2500, "Winddy", message, Forms.ToolTipIcon.None);
    }

    public void ShowBalloon(string message)
    {
        if (!_icon.Visible) return;
        _icon.ShowBalloonTip(5000, "Winddy", message, Forms.ToolTipIcon.Warning);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _normal.Dispose();
        _paused.Dispose();
    }
}
