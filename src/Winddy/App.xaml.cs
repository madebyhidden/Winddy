using System;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Winddy.Services;
using Winddy.Views;

namespace Winddy;

public partial class App : Application
{
    private const string MutexName = "Winddy.SingleInstance";
    private const string ShowEventName = "Winddy.ShowPopup";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private SettingsWindow? _settings;
    private AboutWindow? _about;
    private UpdateWindow? _update;

    public HistoryStore Store { get; } = new();
    public ClipboardService Clipboard { get; private set; } = null!;
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public PopupWindow Popup { get; private set; } = null!;
    public PasteStack PasteStack { get; private set; } = null!;
    public bool IsQuitting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (CommandServer.IsCommand(e.Args))
        {
            Shutdown(CommandServer.RunClient(e.Args));
            return;
        }

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst && Array.IndexOf(e.Args, "--updated") >= 0)
        {
            // Started by the updater: wait for the old version to exit.
            try { isFirst = _mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { isFirst = true; }
        }
        if (!isFirst)
        {
            // Already running: ask the existing instance to show its popup.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* ignore */ }
            Shutdown();
            return;
        }

        AppSettings.Load();
        Loc.Init(AppSettings.Current.Language);
        Theme.Apply(Resources);
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.General) Dispatcher.Invoke(() =>
            {
                Theme.Apply(Resources);
                Theme.RefreshFrame(Popup);
            });
        };

        Updater.CleanupAfterUpdate();
        Store.Load();
        Clipboard = new ClipboardService(Store);
        PasteStack = new PasteStack(Clipboard, Dispatcher);
        Hotkeys = new HotkeyManager();
        Popup = new PopupWindow(this);
        _tray = new TrayIcon(this);
        Notifier.ShowNotification = message => _tray.ShowNotification(message);
        PasteStack.Changed += () => _tray.ApplySettings();

        Hotkeys.Pressed += () => Popup.OnHotkey();
        RegisterHotkey(showError: true);

        Store.Changed += () => _tray.UpdateTooltip(Store.MostRecent);
        AppSettings.Changed += () => _tray.ApplySettings();
        _tray.UpdateTooltip(Store.MostRecent);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (IsQuitting) return;
                Dispatcher.BeginInvoke(() => { if (!Popup.IsOpen) Popup.ShowPopup(fromTray: false); });
            }
        }) { IsBackground = true };
        listener.Start();

        CommandServer.Start(this);
        Updater.StartAutomaticChecks(ShowUpdate);
    }

    public void ShowUpdate(UpdateInfo info)
    {
        if (_update != null)
        {
            _update.Activate();
            return;
        }
        _update = new UpdateWindow(this, info);
        _update.Closed += (_, _) => _update = null;
        _update.Show();
        _update.Activate();
    }

    /// <summary>"Check for updates…": always reports the outcome.</summary>
    public async void CheckForUpdatesNow()
    {
        try
        {
            var info = await Updater.CheckAsync();
            if (info != null) ShowUpdate(info);
            else MessageBox.Show(Loc.T("UpToDate", Updater.CurrentVersion.ToString(3)), "Winddy", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("CheckFailed", ex.Message), "Winddy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void EditItem(Models.HistoryItem item)
    {
        new ItemEditorWindow(Store, item).ShowDialog();
    }

    public bool RegisterHotkey(bool showError)
    {
        var hotkey = Hotkey.Parse(AppSettings.Current.OpenHotkey);
        var ok = Hotkeys.Register(hotkey);
        if (!ok && showError)
            _tray?.ShowBalloon(Loc.T("HotkeyInUse", hotkey.Display));
        return ok;
    }

    public void ShowSettings()
    {
        if (_settings == null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.Show();
        if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
        _settings.Activate();
    }

    public void ShowAbout()
    {
        if (_about == null)
        {
            _about = new AboutWindow();
            _about.Closed += (_, _) => _about = null;
        }
        _about.Show();
        _about.Activate();
    }

    public void ClearHistory(bool all)
    {
        if (!AppSettings.Current.SuppressClearAlert)
        {
            var dialog = new ConfirmClearWindow();
            if (dialog.ShowDialog() != true) return;
            if (dialog.DontAskAgain) AppSettings.Update(s => s.SuppressClearAlert = true);
        }
        Store.Clear(all);
        if (AppSettings.Current.ClearSystemClipboard) ClipboardService.ClearSystemClipboard();
    }

    public void TogglePause() => AppSettings.Update(s => s.IgnoreEvents = !s.IgnoreEvents);

    public void IgnoreNextCopy() => AppSettings.Update(s => s.IgnoreOnlyNextEvent = true);

    public void Quit()
    {
        IsQuitting = true;
        PasteStack?.Stop();
        if (AppSettings.Current.ClearOnQuit)
        {
            Store.Clear(includePinned: false);
            if (AppSettings.Current.ClearSystemClipboard) ClipboardService.ClearSystemClipboard();
        }
        Store.SaveNow();
        _showEvent?.Set();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        Hotkeys?.Dispose();
        // Only an instance that actually loaded the history may write it back.
        if (Clipboard != null)
        {
            Clipboard.Dispose();
            Store.SaveNow();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
