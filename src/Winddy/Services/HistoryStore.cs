using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using Winddy.Models;

namespace Winddy.Services;

/// <summary>Owns the clipboard history: persistence, de-duplication, sorting, pins and trimming.</summary>
public sealed class HistoryStore
{
    // Letters free of the popup's own Ctrl shortcuts (select all, copy, cut, paste, undo, quit,
    // close, emacs-style navigation and search editing).
    public static readonly string[] SupportedPins =
        { "b", "d", "e", "f", "g", "i", "l", "m", "o", "r", "s", "t", "y" };

    private static string FilePath => Path.Combine(AppSettings.Directory, "history.json");
    public static string ImagesDirectory => Path.Combine(AppSettings.Directory, "images");

    private readonly List<HistoryItem> _items = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly Random _random = new();

    public event Action? Changed;

    public HistoryStore()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
    }

    public IReadOnlyList<HistoryItem> All => _items;

    public HistoryItem? MostRecent => _items.Where(i => !i.IsPinned).OrderByDescending(i => i.LastCopiedAt).FirstOrDefault()
                                      ?? _items.OrderByDescending(i => i.LastCopiedAt).FirstOrDefault();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var items = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(FilePath));
                if (items != null) _items.AddRange(items);
            }
        }
        catch
        {
            // A corrupt history file should not prevent the app from starting.
        }
        ReconcilePinOrder();
        CleanupOrphanImages();
    }

    public void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items));
            File.Move(tmp, FilePath, true);
        }
        catch
        {
            // Retry on the next change.
        }
    }

    private void Touch()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        Changed?.Invoke();
    }

    /// <summary>Returns items in display order: pins (in pin order) at top or bottom, the rest sorted.</summary>
    public List<HistoryItem> Sorted()
    {
        var s = AppSettings.Current;
        IEnumerable<HistoryItem> unpinned = _items.Where(i => !i.IsPinned);
        unpinned = s.SortBy switch
        {
            SortBy.FirstCopiedAt => unpinned.OrderByDescending(i => i.FirstCopiedAt),
            SortBy.NumberOfCopies => unpinned.OrderByDescending(i => i.NumberOfCopies).ThenByDescending(i => i.LastCopiedAt),
            _ => unpinned.OrderByDescending(i => i.LastCopiedAt),
        };

        var pinned = _items.Where(i => i.IsPinned).ToList();
        var order = s.PinOrder;
        pinned.Sort((a, b) =>
        {
            var ia = order.IndexOf(a.Pin!);
            var ib = order.IndexOf(b.Pin!);
            return (ia < 0 ? int.MaxValue : ia).CompareTo(ib < 0 ? int.MaxValue : ib);
        });

        return s.PinTo == PinsPosition.Top
            ? pinned.Concat(unpinned).ToList()
            : unpinned.Concat(pinned).ToList();
    }

    /// <summary>Adds a new copy or bumps an existing identical one. Returns the stored item.</summary>
    public HistoryItem Add(HistoryItem item)
    {
        var existing = _items.FirstOrDefault(i => i.Hash == item.Hash);
        if (existing != null)
        {
            existing.LastCopiedAt = item.LastCopiedAt;
            existing.NumberOfCopies++;
            if (item.Rtf != null) existing.Rtf = item.Rtf;
            if (item.Html != null) existing.Html = item.Html;
            if (!string.IsNullOrEmpty(item.Application) && !IsSelf(item.Application)) existing.Application = item.Application;
            if (item.ImageFile != null && item.ImageFile != existing.ImageFile) DeleteImageFile(item);
            Touch();
            return existing;
        }

        if (IsSelf(item.Application)) item.Application = null;
        _items.Add(item);
        Trim();
        Touch();
        return item;
    }

    private static bool IsSelf(string? app) =>
        app != null && string.Equals(Path.GetFileNameWithoutExtension(app), "Winddy", StringComparison.OrdinalIgnoreCase);

    public void Trim()
    {
        var size = Math.Max(1, AppSettings.Current.HistorySize);
        var unpinned = Sorted().Where(i => !i.IsPinned).ToList();
        foreach (var item in unpinned.Skip(size))
            RemoveInternal(item);
    }

    public void Delete(HistoryItem item)
    {
        if (item.IsPinned) Unpin(item, save: false);
        RemoveInternal(item);
        Touch();
    }

    public void Clear(bool includePinned)
    {
        foreach (var item in _items.Where(i => includePinned || !i.IsPinned).ToList())
            RemoveInternal(item);
        if (includePinned) AppSettings.Update(s => s.PinOrder.Clear());
        Touch();
    }

    private void RemoveInternal(HistoryItem item)
    {
        _items.Remove(item);
        DeleteImageFile(item);
    }

    private static void DeleteImageFile(HistoryItem item)
    {
        if (item.ImageFile == null) return;
        try { File.Delete(Path.Combine(ImagesDirectory, item.ImageFile)); } catch { /* ignore */ }
    }

    public IEnumerable<string> AvailablePins(HistoryItem? except = null)
    {
        var used = _items.Where(i => i.IsPinned && i != except).Select(i => i.Pin!).ToHashSet();
        return SupportedPins.Where(p => !used.Contains(p));
    }

    public void TogglePin(HistoryItem item)
    {
        if (item.IsPinned)
        {
            Unpin(item, save: true);
            return;
        }

        var available = AvailablePins().ToList();
        if (available.Count == 0) return;
        item.Pin = available[_random.Next(available.Count)];
        AppSettings.Update(s => { if (!s.PinOrder.Contains(item.Pin)) s.PinOrder.Add(item.Pin); });
        Touch();
    }

    private void Unpin(HistoryItem item, bool save)
    {
        var pin = item.Pin;
        item.Pin = null;
        item.Title = null;
        if (pin != null) AppSettings.Update(s => s.PinOrder.Remove(pin));
        // An unpinned item re-enters the regular history as if just copied.
        item.LastCopiedAt = DateTime.Now;
        if (save)
        {
            Trim();
            Touch();
        }
    }

    public void ChangePin(HistoryItem item, string newPin)
    {
        if (item.Pin == newPin || !SupportedPins.Contains(newPin)) return;
        var holder = _items.FirstOrDefault(i => i.Pin == newPin);
        var old = item.Pin;
        if (holder != null) holder.Pin = old;
        item.Pin = newPin;
        AppSettings.Update(s =>
        {
            var idx = old == null ? -1 : s.PinOrder.IndexOf(old);
            var idxNew = s.PinOrder.IndexOf(newPin);
            if (idx >= 0) s.PinOrder[idx] = newPin;
            if (idxNew >= 0 && old != null) s.PinOrder[idxNew] = old;
        });
        Touch();
    }

    public void UpdateTitle(HistoryItem item, string? title)
    {
        item.Title = string.IsNullOrWhiteSpace(title) ? null : title;
        Touch();
    }

    public void UpdateText(HistoryItem item, string text)
    {
        if (!item.IsPlainTextOnly || item.Text == text) return;
        item.Text = text;
        item.Rtf = null;
        item.Html = null;
        item.Hash = ClipboardService.ComputeHash(text, null, null);
        Touch();
    }

    public void NotifyChanged() => Touch();

    private void ReconcilePinOrder()
    {
        var pins = _items.Where(i => i.IsPinned).Select(i => i.Pin!).ToList();
        AppSettings.Update(s =>
        {
            s.PinOrder = s.PinOrder.Where(pins.Contains).Distinct().ToList();
            foreach (var p in pins.Where(p => !s.PinOrder.Contains(p))) s.PinOrder.Add(p);
        });
    }

    private void CleanupOrphanImages()
    {
        try
        {
            if (!Directory.Exists(ImagesDirectory)) return;
            var used = _items.Where(i => i.ImageFile != null).Select(i => i.ImageFile!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(ImagesDirectory))
                if (!used.Contains(Path.GetFileName(file))) File.Delete(file);
        }
        catch
        {
            // ignore
        }
    }
}
