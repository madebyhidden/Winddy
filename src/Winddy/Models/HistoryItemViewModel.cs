using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Winddy.Services;

namespace Winddy.Models;

/// <summary>A history item as shown in the popup: display title, match ranges, shortcut and selection state.</summary>
public sealed class HistoryItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _shortcut = "";
    private ImageSource? _thumbnail;
    private bool _thumbnailLoaded;

    public HistoryItemViewModel(HistoryItem item, IReadOnlyList<(int Start, int Length)> ranges)
    {
        Item = item;
        var raw = item.SearchableText;
        Title = HistoryItem.ShortenTitle(raw, AppSettings.Current.ShowSpecialSymbols);
        Ranges = MapRanges(raw, ranges);
    }

    public HistoryItem Item { get; }
    public string Title { get; }
    public IReadOnlyList<(int Start, int Length)> Ranges { get; }

    public bool IsImage => Item.HasImage && string.IsNullOrEmpty(Item.Title);
    public Visibility ImageVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TitleVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;
    public double ImageMaxHeight => Math.Max(16, AppSettings.Current.ImageMaxHeight);

    public ImageSource? AppIcon => AppSettings.Current.ShowApplicationIcons ? AppIcons.For(Item.Application) : null;
    public Visibility AppIconVisibility => AppSettings.Current.ShowApplicationIcons ? Visibility.Visible : Visibility.Collapsed;

    public Brush? Swatch
    {
        get
        {
            if (!AppSettings.Current.ShowHexColorSwatch) return null;
            var hex = Item.HexColorValue;
            if (hex == null) return null;
            try
            {
                // CSS-style #RRGGBBAA → WPF #AARRGGBB
                if (hex.Length == 9) hex = "#" + hex[7..9] + hex[1..7];
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            }
            catch
            {
                return null;
            }
        }
    }

    public Visibility SwatchVisibility => Swatch != null ? Visibility.Visible : Visibility.Collapsed;

    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailLoaded && Item.HasImage)
            {
                _thumbnailLoaded = true;
                _thumbnail = LoadImage(Item, (int)(ImageMaxHeight * 2));
            }
            return _thumbnail;
        }
    }

    public static ImageSource? LoadImage(HistoryItem item, int decodeHeight = 0)
    {
        try
        {
            var path = Path.Combine(HistoryStore.ImagesDirectory, item.ImageFile!);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            if (decodeHeight > 0 && item.ImageHeight > decodeHeight) bmp.DecodePixelHeight = decodeHeight;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnChanged(); } }
    }

    private string _badge = "";

    /// <summary>Order number shown while several items are selected (paste stack order).</summary>
    public string Badge
    {
        get => _badge;
        set { if (_badge != value) { _badge = value; OnChanged(); OnChanged(nameof(BadgeVisibility)); } }
    }

    public Visibility BadgeVisibility => _badge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string Shortcut
    {
        get => _shortcut;
        set { if (_shortcut != value) { _shortcut = value; OnChanged(); } }
    }

    /// <summary>Thin line drawn between the pinned and unpinned groups.</summary>
    public bool SeparatorBelow { get; set; }
    public Visibility SeparatorVisibility => SeparatorBelow ? Visibility.Visible : Visibility.Collapsed;

    public string ToolTipText => Item.HasImage ? $"{Loc.T("Image")} {Item.ImageWidth}×{Item.ImageHeight}" : Title;

    /// <summary>
    /// Match ranges refer to the raw text; the title may have had leading whitespace trimmed or
    /// CRLF collapsed, so shift ranges to stay aligned with what's displayed.
    /// </summary>
    private IReadOnlyList<(int Start, int Length)> MapRanges(string raw, IReadOnlyList<(int Start, int Length)> ranges)
    {
        if (ranges.Count == 0) return ranges;
        var map = new int[raw.Length + 1];
        var showSymbols = AppSettings.Current.ShowSpecialSymbols;
        var leading = showSymbols ? 0 : raw.Length - raw.TrimStart().Length;
        int shown = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            map[i] = Math.Max(0, shown - leading);
            if (showSymbols && raw[i] == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n') continue;
            shown++;
        }
        map[raw.Length] = Math.Max(0, shown - leading);

        var result = new List<(int, int)>();
        foreach (var (start, length) in ranges)
        {
            if (start >= raw.Length) continue;
            var s = map[start];
            var e = map[Math.Min(raw.Length, start + length)];
            if (s >= Title.Length) continue;
            e = Math.Min(e, Title.Length);
            if (e > s) result.Add((s, e - s));
        }
        return result;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
