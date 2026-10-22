using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Winddy.Models;
using Winddy.Services;

namespace Winddy.Views;

public enum ItemAction { Unknown, Copy, Paste, PasteWithoutFormatting }

public partial class PopupWindow : Window
{
    private readonly App _app;
    private readonly ObservableCollection<HistoryItemViewModel> _items = new();
    private readonly DispatcherTimer _previewTimer = new();
    private HistoryItemViewModel? _selected;
    private IntPtr _previousWindow;
    private Point _lastMouse = new(double.NaN, double.NaN);
    private bool _cycleMode;
    private bool _positioning;
    private bool _anchorAtCursor;
    private DateTime _hiddenAt;
    // Items picked with Shift/Ctrl, in pick order; empty or one item means a plain selection.
    private readonly List<HistoryItemViewModel> _multi = new();

    public PopupWindow(App app)
    {
        _app = app;
        InitializeComponent();
        List.ItemsSource = _items;
        Theme.StyleWindow(this, acrylic: true);

        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); OpenPreview(); };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Deactivated += (_, _) => { if (IsVisible) HidePopup(); };
        SizeChanged += OnSizeChanged;
        LocationChanged += (_, _) => PreviewPopup.HorizontalOffset += 0.001 * (PreviewPopup.HorizontalOffset > 6 ? -1 : 1);

        _app.Store.Changed += () => { if (IsVisible) Refresh(keepSelection: true); };
        _app.PasteStack.Changed += UpdateStackBanner;
        AppSettings.Changed += ApplyAppearance;
        ApplyAppearance();
    }

    public bool IsOpen => IsVisible;

    // ───────────── Showing / hiding ─────────────

    public void Toggle(bool fromTray)
    {
        if (IsVisible) HidePopup();
        // A tray click first deactivates (and hides) the popup; don't reopen it right away.
        else if ((DateTime.Now - _hiddenAt).TotalMilliseconds > 300) ShowPopup(fromTray);
    }

    /// <summary>Called on every press of the global hotkey.</summary>
    public void OnHotkey()
    {
        if (!IsVisible)
        {
            ShowPopup(fromTray: false);
            return;
        }

        // Holding the modifiers and tapping the key again walks the list; releasing them selects.
        var mods = _app.Hotkeys.Current.Modifiers;
        if (mods != ModifierKeys.None && (Keyboard.Modifiers & mods) == mods)
        {
            _cycleMode = true;
            MoveSelection(+1);
        }
        else
        {
            HidePopup();
        }
    }

    public void ShowPopup(bool fromTray)
    {
        var fg = Native.GetForegroundWindow();
        if (fg != new WindowInteropHelper(this).Handle) _previousWindow = fg;
        _anchorAtCursor = fromTray;
        _cycleMode = false;

        SearchBox.Text = "";
        ApplyAppearance();
        UpdateStackBanner();
        Refresh(keepSelection: false);

        Width = Math.Max(MinWidth, AppSettings.Current.WindowWidth);
        MaxHeight = Math.Max(MinHeight, AppSettings.Current.WindowHeight);
        SizeToContent = SizeToContent.Height;

        _positioning = true;
        Left = -32000;
        Top = -32000;
        Show();
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            PlaceWindow();
            _positioning = false;
            Activate();
            Keyboard.Focus(SearchBox);
        }));
    }

    public void HidePopup()
    {
        _previewTimer.Stop();
        PreviewPopup.IsOpen = false;
        _cycleMode = false;
        if (AppSettings.Current.PopupPosition == PopupPosition.LastPosition && Left > -30000)
        {
            Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
            AppSettings.Update(s => { s.LastPositionX = r.Left; s.LastPositionY = r.Top; });
        }
        Hide();
        _hiddenAt = DateTime.Now;
    }

    private void PlaceWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.GetWindowRect(hwnd, out var rect);
        int w = rect.Width, h = rect.Height;
        Native.GetCursorPos(out var cursor);

        Native.RECT area;
        int x, y;
        var mode = AppSettings.Current.PopupPosition;
        if (_anchorAtCursor && mode == PopupPosition.TrayIcon)
        {
            // Opened from the tray: sit next to the icon, above (or below) the taskbar.
            area = Native.WorkAreaAt(cursor);
            x = cursor.X - w / 2;
            y = cursor.Y > area.Top + area.Height / 2 ? area.Bottom - h : area.Top;
        }
        else switch (mode)
        {
            case PopupPosition.ScreenCenter:
                area = Native.WorkAreaAt(cursor);
                x = area.Left + (area.Width - w) / 2;
                y = area.Top + (area.Height - h) / 2;
                break;
            case PopupPosition.WindowCenter when _previousWindow != IntPtr.Zero && Native.GetWindowRect(_previousWindow, out var fr) && fr.Width > 0:
                area = Native.WorkAreaOfWindow(_previousWindow);
                x = fr.Left + (fr.Width - w) / 2;
                y = fr.Top + (fr.Height - h) / 2;
                break;
            case PopupPosition.TrayIcon:
                area = Native.WorkAreaAt(cursor);
                x = area.Right - w;
                y = area.Bottom - h;
                break;
            case PopupPosition.LastPosition when AppSettings.Current.LastPositionX is { } lx && AppSettings.Current.LastPositionY is { } ly:
                area = Native.WorkAreaAt(new Native.POINT { X = lx, Y = ly });
                x = lx;
                y = ly;
                break;
            default:
                area = Native.WorkAreaAt(cursor);
                x = cursor.X;
                y = cursor.Y;
                break;
        }

        // Keep the popup on one screen.
        x = Math.Min(Math.Max(x, area.Left), area.Right - w);
        y = Math.Min(Math.Max(y, area.Top), area.Bottom - h);
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_positioning || !IsVisible) return;
        // WPF drops SizeToContent once the user drags a border: remember the new size.
        if (SizeToContent == SizeToContent.Manual)
        {
            AppSettings.Current.WindowWidth = ActualWidth;
            AppSettings.Current.WindowHeight = ActualHeight;
            MaxHeight = double.PositiveInfinity;
            AppSettings.Save();
        }
        else
        {
            // Content grew/shrank while open: keep the window inside the screen.
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.GetWindowRect(hwnd, out var r);
            var area = Native.WorkAreaOfWindow(hwnd);
            if (r.Bottom > area.Bottom)
                Native.SetWindowPos(hwnd, IntPtr.Zero, r.Left, Math.Max(area.Top, area.Bottom - r.Height), 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        try { DragMove(); } catch (InvalidOperationException) { /* mouse already released */ }
    }

    // ───────────── Content ─────────────

    private void ApplyAppearance()
    {
        var s = AppSettings.Current;
        TitleText.Visibility = s.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        Footer.Visibility = s.ShowFooter ? Visibility.Visible : Visibility.Collapsed;
        UpdateSearchVisibility();
        UpdateClearLabel(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private void UpdateSearchVisibility()
    {
        var s = AppSettings.Current;
        var visible = s.ShowSearch && (s.SearchVisibility == SearchVisibility.Always || SearchBox.Text.Length > 0);
        // The search box must keep keyboard focus even when hidden, so collapse its row instead of the box.
        Header.Height = visible ? double.NaN : 0;
        Header.Margin = visible ? new Thickness(10, 8, 10, 6) : new Thickness(0, 4, 0, 0);
        Header.Opacity = visible ? 1 : 0;
        Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh(bool keepSelection)
    {
        var previous = keepSelection ? _selected?.Item : null;
        var query = SearchBox.Text;
        var results = Search.Run(query, _app.Store.Sorted());

        // Exact/regex searches keep history order; fuzzy reorders by score, but pins stay grouped.
        var pinTop = AppSettings.Current.PinTo == PinsPosition.Top;
        var pinned = results.Where(r => r.Item.IsPinned).ToList();
        var unpinned = results.Where(r => !r.Item.IsPinned).ToList();
        var ordered = pinTop ? pinned.Concat(unpinned) : unpinned.Concat(pinned);

        _items.Clear();
        var number = 1;
        HistoryItemViewModel? lastPinned = null, firstUnpinned = null;
        foreach (var r in ordered)
        {
            var vm = new HistoryItemViewModel(r.Item, r.Ranges);
            if (r.Item.IsPinned)
            {
                vm.Shortcut = "Ctrl+" + r.Item.Pin!.ToUpperInvariant();
                lastPinned = vm;
            }
            else
            {
                if (number <= 9) vm.Shortcut = "Ctrl+" + number++;
                firstUnpinned ??= vm;
            }
            _items.Add(vm);
        }

        // Divider between the pinned group and the rest of the history.
        if (lastPinned != null && firstUnpinned != null)
        {
            if (pinTop) lastPinned.SeparatorBelow = true;
            else _items[unpinned.Count - 1].SeparatorBelow = true;
        }

        var target = previous != null ? _items.FirstOrDefault(v => v.Item == previous) : null;
        // Maccy highlights the first unpinned item when opened, so a quick Enter re-pastes the latest copy.
        target ??= string.IsNullOrEmpty(query) && !keepSelection ? firstUnpinned ?? _items.FirstOrDefault() : _items.FirstOrDefault();
        Select(target);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchVisibility();
        Refresh(keepSelection: false);
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        Keyboard.Focus(SearchBox);
    }

    // ───────────── Selection ─────────────

    private void Select(HistoryItemViewModel? vm)
    {
        ClearMulti();
        SetLead(vm);
    }

    /// <summary>Moves the highlighted ("lead") item without touching the multi-selection.</summary>
    private void SetLead(HistoryItemViewModel? vm)
    {
        if (_selected == vm) return;
        if (_selected != null && !_multi.Contains(_selected)) _selected.IsSelected = false;
        _selected = vm;
        if (vm == null)
        {
            PreviewPopup.IsOpen = false;
            return;
        }
        vm.IsSelected = true;
        List.ScrollIntoView(vm);

        if (PreviewPopup.IsOpen)
        {
            FillPreview(vm.Item);
        }
        else if (AppSettings.Current.OpenPreviewAutomatically)
        {
            _previewTimer.Stop();
            _previewTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(100, AppSettings.Current.PreviewDelay));
            _previewTimer.Start();
        }
    }

    private void ClearMulti()
    {
        foreach (var m in _multi)
        {
            m.IsSelected = false;
            m.Badge = "";
        }
        _multi.Clear();
    }

    private void UpdateBadges()
    {
        var many = _multi.Count > 1;
        for (var i = 0; i < _multi.Count; i++)
        {
            _multi[i].IsSelected = true;
            _multi[i].Badge = many ? (i + 1).ToString() : "";
        }
    }

    /// <summary>Shift+arrow: add the neighbouring item to the selection (or drop the last one when going back).</summary>
    private void ExtendSelection(int delta)
    {
        if (_selected == null || _items.Count == 0) return;
        if (_multi.Count == 0) _multi.Add(_selected);
        var next = Math.Clamp(_items.IndexOf(_selected) + delta, 0, _items.Count - 1);
        var vm = _items[next];
        if (_multi.Count > 1 && _multi[^2] == vm)
        {
            var dropped = _multi[^1];
            _multi.RemoveAt(_multi.Count - 1);
            dropped.IsSelected = false;
            dropped.Badge = "";
        }
        else if (!_multi.Contains(vm))
        {
            _multi.Add(vm);
        }
        SetLead(vm);
        UpdateBadges();
    }

    /// <summary>Ctrl+click: add or remove a single item.</summary>
    private void ToggleInSelection(HistoryItemViewModel vm)
    {
        if (_multi.Count == 0 && _selected != null) _multi.Add(_selected);
        if (_multi.Contains(vm) && _multi.Count > 1)
        {
            _multi.Remove(vm);
            vm.IsSelected = false;
            vm.Badge = "";
            SetLead(_multi[^1]);
        }
        else if (!_multi.Contains(vm))
        {
            _multi.Add(vm);
            SetLead(vm);
        }
        UpdateBadges();
    }

    /// <summary>Shift+click: select the range between the highlighted item and this one.</summary>
    private void SelectRange(HistoryItemViewModel vm)
    {
        var from = _selected == null ? 0 : _items.IndexOf(_selected);
        var to = _items.IndexOf(vm);
        ClearMulti();
        var step = to >= from ? 1 : -1;
        for (var i = from; ; i += step)
        {
            _multi.Add(_items[i]);
            if (i == to) break;
        }
        SetLead(vm);
        UpdateBadges();
    }

    private List<HistoryItemViewModel> SelectedItems =>
        _multi.Count > 1 ? _multi.ToList() : _selected == null ? new() : new() { _selected };

    private void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        var idx = _selected == null ? -1 : _items.IndexOf(_selected);
        idx = Math.Clamp(idx + delta, 0, _items.Count - 1);
        Select(_items[idx]);
    }

    private void SelectEdge(bool first)
    {
        if (_items.Count == 0) return;
        Select(first ? _items[0] : _items[^1]);
    }

    private void Row_MouseMove(object sender, MouseEventArgs e)
    {
        // Only react to real mouse movement, not to the list scrolling under a resting cursor.
        var p = e.GetPosition(this);
        if (p == _lastMouse) return;
        _lastMouse = p;
        if (((FrameworkElement)sender).DataContext is HistoryItemViewModel vm) Select(vm);
    }

    private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not HistoryItemViewModel vm) return;
        switch (Keyboard.Modifiers)
        {
            case ModifierKeys.Control:
                ToggleInSelection(vm);
                break;
            case ModifierKeys.Shift:
                SelectRange(vm);
                break;
            default:
                if (_multi.Count > 1 && _multi.Contains(vm)) StartPasteStack(Keyboard.Modifiers);
                else Choose(vm, Keyboard.Modifiers, defaultIfUnknown: true);
                break;
        }
    }

    private void RowMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender;
        if (menu.DataContext is HistoryItemViewModel vm && menu.Items[0] is MenuItem pin)
            pin.Header = Loc.T(vm.Item.IsPinned ? "Unpin" : "Pin");
    }

    private void PinMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is HistoryItemViewModel vm) _app.Store.TogglePin(vm.Item);
    }

    private void EditMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is HistoryItemViewModel vm) EditItem(vm.Item);
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is HistoryItemViewModel vm) DeleteItem(vm);
    }

    // ───────────── Actions ─────────────

    /// <summary>Maps modifiers held while choosing an item to copy/paste behavior (Ctrl ≙ ⌘, Alt ≙ ⌥).</summary>
    public static ItemAction ActionFor(ModifierKeys mods)
    {
        var s = AppSettings.Current;
        bool paste = s.PasteByDefault, plain = s.RemoveFormattingByDefault;
        return mods switch
        {
            ModifierKeys.Control when !paste => ItemAction.Copy,
            ModifierKeys.Control => plain ? ItemAction.PasteWithoutFormatting : ItemAction.Paste,
            ModifierKeys.Alt when !paste => plain ? ItemAction.PasteWithoutFormatting : ItemAction.Paste,
            ModifierKeys.Alt => ItemAction.Copy,
            ModifierKeys.Alt | ModifierKeys.Shift when !paste => plain ? ItemAction.Paste : ItemAction.PasteWithoutFormatting,
            ModifierKeys.Control | ModifierKeys.Shift when paste => plain ? ItemAction.Paste : ItemAction.PasteWithoutFormatting,
            _ => ItemAction.Unknown,
        };
    }

    public static string ModifierLabel(ItemAction action)
    {
        foreach (var mods in new[] { ModifierKeys.Control, ModifierKeys.Alt, ModifierKeys.Alt | ModifierKeys.Shift, ModifierKeys.Control | ModifierKeys.Shift })
        {
            if (ActionFor(mods) != action) continue;
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            return string.Join("+", parts);
        }
        return "";
    }

    private void Choose(HistoryItemViewModel? vm, ModifierKeys mods, bool defaultIfUnknown = false)
    {
        if (vm == null) return;
        if (!ResolveAction(mods, defaultIfUnknown, out var action, out var removeFormatting)) return;

        var target = _previousWindow;
        HidePopup();
        _app.Clipboard.Copy(vm.Item, removeFormatting);
        if (action != ItemAction.Copy) _ = ClipboardService.PasteAsync(target);
    }

    private static bool ResolveAction(ModifierKeys mods, bool defaultIfUnknown, out ItemAction action, out bool removeFormatting)
    {
        var s = AppSettings.Current;
        removeFormatting = false;
        if (mods == ModifierKeys.None)
        {
            action = s.PasteByDefault ? ItemAction.Paste : ItemAction.Copy;
            removeFormatting = s.RemoveFormattingByDefault;
        }
        else
        {
            action = ActionFor(mods);
            if (action == ItemAction.Unknown)
            {
                if (!defaultIfUnknown) return false;
                action = s.PasteByDefault ? ItemAction.Paste : ItemAction.Copy;
            }
            removeFormatting = action == ItemAction.PasteWithoutFormatting;
        }
        return true;
    }

    /// <summary>Several items chosen: queue them so consecutive Ctrl+V presses paste them in order.</summary>
    private async void StartPasteStack(ModifierKeys mods)
    {
        if (!ResolveAction(mods, true, out var action, out var removeFormatting)) return;
        var items = _multi.Select(v => v.Item).ToList();
        var target = _previousWindow;
        HidePopup();
        _app.PasteStack.Start(items, removeFormatting);
        if (action == ItemAction.Copy) return;
        await ClipboardService.PasteAsync(target);
        await System.Threading.Tasks.Task.Delay(150);
        _app.PasteStack.Advance();
    }

    private void UpdateStackBanner()
    {
        var stack = _app.PasteStack;
        StackBanner.Visibility = stack.IsActive ? Visibility.Visible : Visibility.Collapsed;
        List.Margin = stack.IsActive ? new Thickness(0, 34, 0, 0) : new Thickness(0);
        StackText.Text = Loc.T("PasteStackLeft", stack.Remaining);
    }

    private void StopStack_Click(object sender, RoutedEventArgs e) => _app.PasteStack.Stop();

    private void EditItem(HistoryItem item)
    {
        HidePopup();
        _app.EditItem(item);
    }

    private void DeleteSelection()
    {
        var items = SelectedItems;
        if (items.Count == 0) return;
        var idx = _items.IndexOf(items[0]);
        foreach (var vm in items) _app.Store.Delete(vm.Item);
        if (_items.Count > 0) Select(_items[Math.Clamp(idx, 0, _items.Count - 1)]);
    }

    private void TogglePinSelection()
    {
        var items = SelectedItems.Select(v => v.Item).ToList();
        if (items.Count == 0) return;
        // Like Maccy: unpin when everything selected is pinned, otherwise pin the rest.
        var unpin = items.All(i => i.IsPinned);
        foreach (var item in items.Where(i => i.IsPinned == unpin)) _app.Store.TogglePin(item);
    }

    private void DeleteItem(HistoryItemViewModel vm)
    {
        var idx = _items.IndexOf(vm);
        _app.Store.Delete(vm.Item);
        if (_items.Count > 0) Select(_items[Math.Clamp(idx, 0, _items.Count - 1)]);
    }

    private void Clear_Click(object sender, MouseButtonEventArgs e) => ClearHistory(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

    private void ClearHistory(bool all)
    {
        HidePopup();
        _app.ClearHistory(all);
    }

    private void Preferences_Click(object sender, MouseButtonEventArgs e) { HidePopup(); _app.ShowSettings(); }

    private void About_Click(object sender, MouseButtonEventArgs e) { HidePopup(); _app.ShowAbout(); }

    private void Quit_Click(object sender, MouseButtonEventArgs e) => _app.Quit();

    private void UpdateClearLabel(bool shift)
    {
        ClearLabel.Text = Loc.T(shift ? "ClearAll" : "Clear");
        ClearShortcut.Text = shift ? "Ctrl+Alt+Shift+Backspace" : "Ctrl+Alt+Backspace";
        ClearRow.ToolTip = Loc.T(shift ? "ClearAllTooltip" : "ClearTooltip");
    }

    // ───────────── Preview ─────────────

    private void TogglePreview()
    {
        if (PreviewPopup.IsOpen) { PreviewPopup.IsOpen = false; return; }
        OpenPreview();
    }

    private void OpenPreview()
    {
        if (_selected == null || !IsVisible) return;
        FillPreview(_selected.Item);
        PreviewPopup.IsOpen = true;
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Item.RecognizedText is not { } text) return;
        HidePopup();
        _app.Clipboard.CopyText(text);
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => TogglePinSelection();

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null) EditItem(_selected.Item);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelection();

    private void FillPreview(HistoryItem item)
    {
        var s = AppSettings.Current;
        CopyTextButton.Visibility = item.HasImage ? Visibility.Visible : Visibility.Collapsed;
        CopyTextButton.IsEnabled = item.RecognizedText != null;
        CopyTextButton.ToolTip = Loc.T("CopyExtractedText");
        PinButton.Content = item.IsPinned ? "\uE77A" : "\uE718";
        PinButton.ToolTip = $"{Loc.T(item.IsPinned ? "Unpin" : "Pin")} ({Hotkey.Parse(s.PinHotkey).Display})";
        EditButton.IsEnabled = item.IsPlainTextOnly || item.IsPinned;
        EditButton.ToolTip = Loc.T("EditItem");
        DeleteButton.ToolTip = $"{Loc.T("Delete")} ({Hotkey.Parse(s.DeleteHotkey).Display})";

        if (item.HasImage)
        {
            PreviewImage.Source = HistoryItemViewModel.LoadImage(item);
            PreviewImage.Visibility = Visibility.Visible;
            PreviewText.Visibility = Visibility.Collapsed;
        }
        else
        {
            var text = item.HasFiles ? string.Join(Environment.NewLine, item.Files!) : item.Text ?? "";
            PreviewText.Text = text.Length > 20000 ? text[..20000] + "…" : text;
            PreviewText.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
        }

        var info = new StringBuilder();
        if (item.HasImage) info.AppendLine($"{item.ImageWidth}×{item.ImageHeight}");
        if (!string.IsNullOrEmpty(item.ApplicationName)) info.AppendLine($"{Loc.T("PreviewApplication")} {item.ApplicationName}");
        info.AppendLine($"{Loc.T("PreviewFirstCopy")} {item.FirstCopiedAt:g}");
        info.AppendLine($"{Loc.T("PreviewLastCopy")} {item.LastCopiedAt:g}");
        info.AppendLine($"{Loc.T("PreviewNumberOfCopies")} {item.NumberOfCopies}");
        info.AppendLine();
        info.AppendLine(Loc.T("PreviewPinKey", Hotkey.Parse(AppSettings.Current.PinHotkey).Display));
        info.Append(Loc.T("PreviewDeleteKey", Hotkey.Parse(AppSettings.Current.DeleteHotkey).Display));
        PreviewInfo.Text = info.ToString();
    }

    // ───────────── Keyboard ─────────────

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = Hotkey.RealKey(e);
        if (key is Key.LeftShift or Key.RightShift) UpdateClearLabel(false);

        if (_cycleMode && Hotkey.IsModifierKey(key))
        {
            var mods = _app.Hotkeys.Current.Modifiers;
            if ((Keyboard.Modifiers & mods) == ModifierKeys.None)
            {
                _cycleMode = false;
                Choose(_selected, ModifierKeys.None);
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = Hotkey.RealKey(e);
        var mods = Keyboard.Modifiers;
        if (key is Key.LeftShift or Key.RightShift) UpdateClearLabel(true);
        if (Hotkey.IsModifierKey(key)) return;

        e.Handled = true;
        var s = AppSettings.Current;

        // Configurable item shortcuts first, so they win over everything else.
        if (Hotkey.Parse(s.DeleteHotkey).Matches(key, mods) || (key == Key.Delete && mods == ModifierKeys.Alt && s.DeleteHotkey == "Alt+Back"))
        {
            DeleteSelection();
            return;
        }
        if (Hotkey.Parse(s.PinHotkey).Matches(key, mods))
        {
            TogglePinSelection();
            return;
        }
        if (Hotkey.Parse(s.PreviewHotkey).Matches(key, mods))
        {
            TogglePreview();
            return;
        }

        switch (key, mods)
        {
            case (Key.Back, ModifierKeys.Control | ModifierKeys.Alt):
                ClearHistory(all: false);
                return;
            case (Key.Back, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift):
                ClearHistory(all: true);
                return;
            case (Key.U, ModifierKeys.Control):
                SearchBox.Clear();
                return;
            case (Key.H, ModifierKeys.Control):
                if (SearchBox.Text.Length > 0) SetSearch(SearchBox.Text[..^1]);
                return;
            case (Key.W, ModifierKeys.Control):
                SetSearch(DeleteLastWord(SearchBox.Text));
                return;
            case (Key.Down, ModifierKeys.Shift):
            case (Key.N, ModifierKeys.Control | ModifierKeys.Shift):
                ExtendSelection(+1);
                return;
            case (Key.Up, ModifierKeys.Shift):
            case (Key.P, ModifierKeys.Control | ModifierKeys.Shift):
                ExtendSelection(-1);
                return;
            case (Key.Down, ModifierKeys.None):
            case (Key.N, ModifierKeys.Control):
            case (Key.J, ModifierKeys.Control):
                MoveSelection(+1);
                return;
            case (Key.Up, ModifierKeys.None):
            case (Key.P, ModifierKeys.Control):
            case (Key.K, ModifierKeys.Control):
                MoveSelection(-1);
                return;
            case (Key.PageDown, ModifierKeys.None):
            case (Key.End, ModifierKeys.Control):
            case (Key.Down, ModifierKeys.Control):
            case (Key.Down, ModifierKeys.Alt):
                SelectEdge(first: false);
                return;
            case (Key.PageUp, ModifierKeys.None):
            case (Key.Home, ModifierKeys.Control):
            case (Key.Up, ModifierKeys.Control):
            case (Key.Up, ModifierKeys.Alt):
                SelectEdge(first: true);
                return;
            case (Key.OemComma, ModifierKeys.Control):
                HidePopup();
                _app.ShowSettings();
                return;
            case (Key.Q, ModifierKeys.Control):
                _app.Quit();
                return;
            case (Key.Escape, _):
                if (PreviewPopup.IsOpen) PreviewPopup.IsOpen = false;
                else if (_multi.Count > 1) Select(_selected);
                else HidePopup();
                return;
            case (Key.Enter, _):
                if (_multi.Count > 1) StartPasteStack(mods);
                else if (_selected != null) Choose(_selected, mods);
                else if (SearchBox.Text.Length > 0)
                {
                    // Nothing matches: copy what was typed, like Maccy.
                    var text = SearchBox.Text;
                    HidePopup();
                    _app.Clipboard.CopyText(text);
                }
                return;
        }

        // Ctrl/Alt (+Shift) + 1…9 picks the n-th history item; + letter picks a pinned item.
        if (mods != ModifierKeys.None && mods != ModifierKeys.Shift && ActionFor(mods) != ItemAction.Unknown)
        {
            var label = NumberOrLetter(key);
            if (label != null)
            {
                var vm = _items.FirstOrDefault(v => v.Shortcut.EndsWith("+" + label, StringComparison.Ordinal));
                if (vm != null)
                {
                    Choose(vm, mods);
                    return;
                }
            }
        }

        // Anything else is typing into the search field.
        e.Handled = false;
        if (!SearchBox.IsKeyboardFocusWithin) Keyboard.Focus(SearchBox);
    }

    private static string? NumberOrLetter(Key key)
    {
        if (key is >= Key.D1 and <= Key.D9) return ((char)('1' + (key - Key.D1))).ToString();
        if (key is >= Key.NumPad1 and <= Key.NumPad9) return ((char)('1' + (key - Key.NumPad1))).ToString();
        if (key is >= Key.A and <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
        return null;
    }

    private void SetSearch(string text)
    {
        SearchBox.Text = text;
        SearchBox.CaretIndex = text.Length;
    }

    private static string DeleteLastWord(string text)
    {
        var t = text.TrimEnd();
        var idx = t.LastIndexOf(' ');
        return idx < 0 ? "" : t[..(idx + 1)];
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The popup lives for the whole session; closing just hides it.
        if (!_app.IsQuitting)
        {
            e.Cancel = true;
            HidePopup();
        }
        base.OnClosing(e);
    }
}
