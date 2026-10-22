using System;
using System.Drawing;
using System.Windows;
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
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly Forms.ToolStripMenuItem _stopStackItem;
    private string _recent = "";

    public TrayIcon(App app)
    {
        _app = app;
        _normal = LoadIcon("Winddy.ico");
        _paused = LoadIcon("WinddyPaused.ico");

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Open"), null, (_, _) => _app.Popup.ShowPopup(fromTray: true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        _pauseItem = new Forms.ToolStripMenuItem(Loc.T("Pause"), null, (_, _) => _app.TogglePause());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(Loc.T("IgnoreNext"), null, (_, _) => _app.IgnoreNextCopy());
        _stopStackItem = new Forms.ToolStripMenuItem(Loc.T("StopPasteStack"), null, (_, _) => _app.PasteStack.Stop()) { Visible = false };
        menu.Items.Add(_stopStackItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Preferences"), null, (_, _) => _app.ShowSettings());
        menu.Items.Add(Loc.T("CheckForUpdatesMenu"), null, (_, _) => _app.CheckForUpdatesNow());
        menu.Items.Add(Loc.T("About"), null, (_, _) => _app.ShowAbout());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Quit"), null, (_, _) => _app.Quit());

        _icon = new Forms.NotifyIcon { ContextMenuStrip = menu };
        _icon.MouseUp += OnMouseUp;
        ApplySettings();
    }

    private static Icon LoadIcon(string name)
    {
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
        return new Icon(info!.Stream, Forms.SystemInformation.SmallIconSize);
    }

    private void OnMouseUp(object? sender, Forms.MouseEventArgs e)
    {
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
        _pauseItem.Text = Loc.T(paused ? "Resume" : "Pause");
        _icon.Visible = s.ShowInTray;
        _stopStackItem.Visible = _app.PasteStack?.IsActive == true;
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
