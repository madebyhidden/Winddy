using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Winddy.Models;
using Winddy.Services;

namespace Winddy.Views;

public sealed record Choice(object Value, string Label);

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly ObservableCollection<PinRow> _pins = new();
    private bool _savePending;
    private bool _loading = true;

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        SourceInitialized += (_, _) => Theme.StyleWindow(this);

        SearchModeBox.ItemsSource = Choices<SearchMode>(("Exact", SearchMode.Exact), ("Fuzzy", SearchMode.Fuzzy), ("Regexp", SearchMode.Regexp), ("Mixed", SearchMode.Mixed));
        SortByBox.ItemsSource = Choices<SortBy>(("LastCopiedAt", SortBy.LastCopiedAt), ("FirstCopiedAt", SortBy.FirstCopiedAt), ("NumberOfCopies", SortBy.NumberOfCopies));
        PopupPositionBox.ItemsSource = Choices<PopupPosition>(("Cursor", PopupPosition.Cursor), ("TrayIcon", PopupPosition.TrayIcon),
            ("WindowCenter", PopupPosition.WindowCenter), ("ScreenCenter", PopupPosition.ScreenCenter), ("LastPosition", PopupPosition.LastPosition));
        PinToBox.ItemsSource = Choices<PinsPosition>(("Top", PinsPosition.Top), ("Bottom", PinsPosition.Bottom));
        HighlightBox.ItemsSource = Choices<HighlightMatch>(("Bold", HighlightMatch.Bold), ("Italic", HighlightMatch.Italic),
            ("Underline", HighlightMatch.Underline), ("Color", HighlightMatch.Color));
        SearchVisibilityBox.ItemsSource = Choices<SearchVisibility>(("Always", SearchVisibility.Always), ("DuringSearch", SearchVisibility.DuringSearch));
        LanguageBox.ItemsSource = new List<Choice> { new("", Loc.T("LanguageSystem")), new("en", "English"), new("ru", "Русский") };
        LanguageBox.SelectedValue = AppSettings.Current.Language ?? "";

        DataContext = AppSettings.Current;
        LaunchAtLoginBox.IsChecked = LaunchAtLogin.IsEnabled;

        AppsList.ItemsSource = AppSettings.Current.IgnoredApps.ToList();
        FormatsList.ItemsSource = AppSettings.Current.IgnoredFormats.ToList();
        RegexList.ItemsSource = AppSettings.Current.IgnoreRegexp.ToList();

        PinsGrid.ItemsSource = _pins;
        LoadPins();
        UpdateModifiersText();
        UpdateDiskSize();

        OpenHotkeyBox.RecordingStarted += (_, _) => _app.Hotkeys.Suspend();
        OpenHotkeyBox.RecordingEnded += (_, _) => _app.RegisterHotkey(showError: true);

        // Every bound control writes straight into AppSettings.Current; persist after any edit.
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnAnyChange));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnAnyChange));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => OnAnyChange(s, e)));
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => OnAnyChange(s, e)));

        _app.Store.Changed += OnStoreChanged;
        Closed += (_, _) => _app.Store.Changed -= OnStoreChanged;
        Loaded += (_, _) => _loading = false;
    }

    private static List<Choice> Choices<T>(params (string Key, T Value)[] items) where T : notnull =>
        items.Select(i => new Choice(i.Value, Loc.T(i.Key))).ToList();

    private void OnAnyChange(object sender, RoutedEventArgs e)
    {
        if (_loading || _savePending) return;
        if (e.OriginalSource is DataGridCell or DataGrid || IsInside(e.OriginalSource as DependencyObject, PinsGrid)) return;
        _savePending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _savePending = false;
            AppSettings.Save();
            _app.Store.Trim();
            UpdateModifiersText();
            if (Hotkey.Parse(AppSettings.Current.OpenHotkey) != _app.Hotkeys.Current && !OpenHotkeyBox.IsKeyboardFocused)
                _app.RegisterHotkey(showError: true);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static bool IsInside(DependencyObject? element, DependencyObject container)
    {
        while (element != null)
        {
            if (element == container) return true;
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private void UpdateModifiersText()
    {
        ModifiersText.Text = Loc.T("Modifiers",
            PopupWindow.ModifierLabel(ItemAction.Copy),
            PopupWindow.ModifierLabel(ItemAction.Paste),
            PopupWindow.ModifierLabel(ItemAction.PasteWithoutFormatting));
    }

    private void UpdateDiskSize()
    {
        try
        {
            var dir = new DirectoryInfo(AppSettings.Directory);
            var bytes = dir.Exists ? dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
            DiskSizeText.Text = bytes switch
            {
                < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
                _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
            };
        }
        catch
        {
            DiskSizeText.Text = "";
        }
    }

    private void LaunchAtLogin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LaunchAtLogin.IsEnabled = LaunchAtLoginBox.IsChecked == true;
        }
        catch
        {
            LaunchAtLoginBox.IsChecked = LaunchAtLogin.IsEnabled;
        }
    }

    private void CheckNow_Click(object sender, RoutedEventArgs e) => _app.CheckForUpdatesNow();

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var lang = LanguageBox.SelectedValue as string;
        AppSettings.Current.Language = string.IsNullOrEmpty(lang) ? null : lang;
        RestartHint.Visibility = Visibility.Visible;
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e) =>
        AppSettings.Update(s => { s.LastPositionX = null; s.LastPositionY = null; s.WindowWidth = 450; s.WindowHeight = 800; });

    // ───────────── Pins ─────────────

    private void OnStoreChanged()
    {
        if (!PinsGrid.IsKeyboardFocusWithin) LoadPins();
        UpdateDiskSize();
    }

    private void LoadPins()
    {
        _pins.Clear();
        foreach (var item in _app.Store.Sorted().Where(i => i.IsPinned))
            _pins.Add(new PinRow(item, _app.Store));
    }

    private void MovePin(int delta)
    {
        if (PinsGrid.SelectedItem is not PinRow row) return;
        var order = AppSettings.Current.PinOrder;
        var idx = order.IndexOf(row.Item.Pin!);
        var target = idx + delta;
        if (idx < 0 || target < 0 || target >= order.Count) return;
        (order[idx], order[target]) = (order[target], order[idx]);
        AppSettings.Save();
        _app.Store.NotifyChanged();
        LoadPins();
        PinsGrid.SelectedItem = _pins.FirstOrDefault(p => p.Item == row.Item);
    }

    private void PinUp_Click(object sender, RoutedEventArgs e) => MovePin(-1);

    private void PinDown_Click(object sender, RoutedEventArgs e) => MovePin(+1);

    // ───────────── Ignore lists ─────────────

    private static void AddTo(List<string> target, ListBox view, TextBox input)
    {
        var value = input.Text.Trim();
        if (value.Length == 0 || target.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        target.Add(value);
        view.ItemsSource = target.ToList();
        input.Clear();
        AppSettings.Save();
    }

    private static void RemoveFrom(List<string> target, ListBox view)
    {
        if (view.SelectedItem is not string value) return;
        target.Remove(value);
        view.ItemsSource = target.ToList();
        AppSettings.Save();
    }

    private void AddApp_Click(object sender, RoutedEventArgs e) => AddTo(AppSettings.Current.IgnoredApps, AppsList, AppInput);

    private void RemoveApp_Click(object sender, RoutedEventArgs e) => RemoveFrom(AppSettings.Current.IgnoredApps, AppsList);

    private void AppInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddApp_Click(sender, e);
    }

    private void BrowseApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Applications (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        AppInput.Text = Path.GetFileName(dialog.FileName);
        AddApp_Click(sender, e);
    }

    private void AddFormat_Click(object sender, RoutedEventArgs e) => AddTo(AppSettings.Current.IgnoredFormats, FormatsList, FormatInput);

    private void RemoveFormat_Click(object sender, RoutedEventArgs e) => RemoveFrom(AppSettings.Current.IgnoredFormats, FormatsList);

    private void FormatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddFormat_Click(sender, e);
    }

    private void ResetFormats_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.IgnoredFormats = new List<string>(AppSettings.DefaultIgnoredFormats);
        FormatsList.ItemsSource = AppSettings.Current.IgnoredFormats.ToList();
        AppSettings.Save();
    }

    private void AddRegex_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = new System.Text.RegularExpressions.Regex(RegexInput.Text);
        }
        catch (ArgumentException ex)
        {
            RegexInput.ToolTip = ex.Message;
            return;
        }
        RegexInput.ToolTip = null;
        AddTo(AppSettings.Current.IgnoreRegexp, RegexList, RegexInput);
    }

    private void RemoveRegex_Click(object sender, RoutedEventArgs e) => RemoveFrom(AppSettings.Current.IgnoreRegexp, RegexList);

    private void RegexInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddRegex_Click(sender, e);
    }
}

/// <summary>Editable row of the Pins table.</summary>
public sealed class PinRow : INotifyPropertyChanged
{
    private readonly HistoryStore _store;

    public PinRow(HistoryItem item, HistoryStore store)
    {
        Item = item;
        _store = store;
    }

    public HistoryItem Item { get; }

    public List<string> AvailablePins =>
        _store.AvailablePins(Item).Append(Item.Pin!).Distinct().OrderBy(p => p).Select(p => p.ToUpperInvariant()).ToList();

    public string Key
    {
        get => Item.Pin?.ToUpperInvariant() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            _store.ChangePin(Item, value.ToLowerInvariant());
            OnChanged();
        }
    }

    public string? Title
    {
        get => Item.Title;
        set { _store.UpdateTitle(Item, value); OnChanged(); }
    }

    public bool IsEditable => Item.IsPlainTextOnly;

    public string Content
    {
        get => IsEditable ? Item.Text ?? "" : Loc.T("ContentIsNotText");
        set
        {
            if (!IsEditable) return;
            _store.UpdateText(Item, value);
            OnChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
