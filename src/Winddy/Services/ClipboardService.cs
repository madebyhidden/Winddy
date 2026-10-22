using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Winddy.Models;

namespace Winddy.Services;

/// <summary>Watches the system clipboard, turns changes into history items, and writes items back.</summary>
public sealed class ClipboardService : IDisposable
{
    private readonly HistoryStore _store;
    private readonly HwndSource _source;
    private readonly DispatcherTimer _debounce;

    /// <summary>Raised for every recorded copy; the flag is true when the item is new to the history.</summary>
    public event Action<HistoryItem, bool>? Copied;

    public ClipboardService(HistoryStore store)
    {
        _store = store;
        var parameters = new HwndSourceParameters("WinddyClipboardListener") { ParentWindow = Native.HWND_MESSAGE, WindowStyle = 0 };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        Native.AddClipboardFormatListener(_source.Handle);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(10, AppSettings.Current.ClipboardCheckDelay)) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); OnClipboardChanged(); };
    }

    public void Dispose()
    {
        Native.RemoveClipboardFormatListener(_source.Handle);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            // Apps often write several formats in quick succession; read once they're done.
            _debounce.Stop();
            _debounce.Start();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnClipboardChanged()
    {
        var settings = AppSettings.Current;
        if (settings.IgnoreEvents) return;
        if (settings.IgnoreOnlyNextEvent)
        {
            AppSettings.Update(s => s.IgnoreOnlyNextEvent = false);
            return;
        }

        var app = SourceApplication();
        if (ShouldIgnoreApp(app)) return;
        if (HasIgnoredFormat()) return;

        HistoryItem? item;
        try
        {
            item = ReadClipboard(app);
        }
        catch
        {
            return;
        }
        if (item == null) return;

        var stored = _store.Add(item);
        var isNew = stored == item;
        Copied?.Invoke(stored, isNew);
        if (isNew) Notifier.Notify(HistoryItem.ShortenTitle(stored.SearchableText, false), NotifySound.Write);
        if (isNew && stored.HasImage && stored.RecognizedText == null) _ = RecognizeAsync(stored);
    }

    private async Task RecognizeAsync(HistoryItem item)
    {
        var text = await TextRecognizer.RecognizeAsync(Path.Combine(HistoryStore.ImagesDirectory, item.ImageFile!));
        if (text == null || !_store.All.Contains(item)) return;
        item.RecognizedText = text;
        _store.NotifyChanged();
    }

    private static string? SourceApplication()
    {
        var path = Native.ProcessPathOfWindow(Native.GetClipboardOwner());
        return path ?? Native.ProcessPathOfWindow(Native.GetForegroundWindow());
    }

    private static bool ShouldIgnoreApp(string? app)
    {
        var s = AppSettings.Current;
        var name = app == null ? "" : Path.GetFileName(app);
        var listed = s.IgnoredApps.Any(a =>
            string.Equals(a, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, app, StringComparison.OrdinalIgnoreCase));
        return s.IgnoreAllAppsExceptListed ? !listed : listed;
    }

    /// <summary>Checks registered format names against the ignore lists, like Maccy does with pasteboard types.</summary>
    private bool HasIgnoredFormat()
    {
        var ignored = new HashSet<string>(AppSettings.AlwaysIgnoredFormats.Concat(AppSettings.Current.IgnoredFormats), StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!Native.OpenClipboard(_source.Handle))
            {
                Thread.Sleep(20);
                continue;
            }
            try
            {
                uint format = 0;
                var sb = new StringBuilder(256);
                while ((format = Native.EnumClipboardFormats(format)) != 0)
                {
                    sb.Clear();
                    if (Native.GetClipboardFormatName(format, sb, sb.Capacity) <= 0) continue;
                    var name = sb.ToString();
                    if (ignored.Contains(name)) return true;
                    if (name == "CanIncludeInClipboardHistory" && ReadDword(format) == 0) return true;
                }
                return false;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }
        return false;
    }

    private static int? ReadDword(uint format)
    {
        var handle = Native.GetClipboardData(format);
        if (handle == IntPtr.Zero) return null;
        var ptr = Native.GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            return (ulong)Native.GlobalSize(handle) >= 4 ? Marshal.ReadInt32(ptr) : null;
        }
        finally
        {
            Native.GlobalUnlock(handle);
        }
    }

    private static T Retry<T>(Func<T> action)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                return action();
            }
            catch (COMException) when (i < 10)
            {
                Thread.Sleep(30);
            }
            catch (ExternalException) when (i < 10)
            {
                Thread.Sleep(30);
            }
        }
    }

    private static HistoryItem? ReadClipboard(string? app)
    {
        var s = AppSettings.Current;
        var data = Retry(Clipboard.GetDataObject);
        if (data == null) return null;

        string? text = null, rtf = null, html = null;
        List<string>? files = null;
        byte[]? png = null;
        int width = 0, height = 0;

        if (s.SaveFiles && data.GetDataPresent(DataFormats.FileDrop))
        {
            if (data.GetData(DataFormats.FileDrop) is string[] dropped && dropped.Length > 0)
                files = dropped.ToList();
        }

        if (s.SaveText)
        {
            if (data.GetDataPresent(DataFormats.UnicodeText)) text = data.GetData(DataFormats.UnicodeText) as string;
            else if (data.GetDataPresent(DataFormats.Text)) text = data.GetData(DataFormats.Text) as string;
            if (data.GetDataPresent(DataFormats.Rtf)) rtf = data.GetData(DataFormats.Rtf) as string;
            if (data.GetDataPresent(DataFormats.Html)) html = data.GetData(DataFormats.Html) as string;
        }

        // Office apps put a rendered bitmap next to the copied text; the text is what matters there.
        if (s.SaveImages && files == null && string.IsNullOrEmpty(text))
            png = ReadImage(data, out width, out height);

        if (string.IsNullOrEmpty(text) && files == null && png == null) return null;
        if (!string.IsNullOrEmpty(text) && files == null && png == null && IsIgnoredByRegex(text!)) return null;

        var item = new HistoryItem
        {
            Text = string.IsNullOrEmpty(text) ? null : text,
            Rtf = rtf,
            Html = html,
            Files = files,
            Application = app,
            ImageWidth = width,
            ImageHeight = height,
            Hash = ComputeHash(text, files, png),
        };

        if (png != null)
        {
            Directory.CreateDirectory(HistoryStore.ImagesDirectory);
            item.ImageFile = item.Id.ToString("N") + ".png";
            File.WriteAllBytes(Path.Combine(HistoryStore.ImagesDirectory, item.ImageFile), png);
        }

        return item;
    }

    private static bool IsIgnoredByRegex(string text)
    {
        foreach (var pattern in AppSettings.Current.IgnoreRegexp)
        {
            try
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200))) return true;
            }
            catch
            {
                // Invalid pattern or timeout: skip it.
            }
        }
        return false;
    }

    private static byte[]? ReadImage(IDataObject data, out int width, out int height)
    {
        width = height = 0;
        BitmapSource? bitmap = null;
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream pngStream)
            {
                var decoder = new PngBitmapDecoder(pngStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                bitmap = decoder.Frames[0];
            }
            else if (data.GetDataPresent(DataFormats.Bitmap))
            {
                bitmap = Retry(Clipboard.GetImage);
            }
        }
        catch
        {
            return null;
        }
        if (bitmap == null) return null;

        width = bitmap.PixelWidth;
        height = bitmap.PixelHeight;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static string ComputeHash(string? text, IEnumerable<string>? files, byte[]? image)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        sb.Append("t:").Append(text ?? "").Append('\0');
        if (files != null) sb.Append("f:").Append(string.Join("|", files)).Append('\0');
        if (image != null) sb.Append("i:").Append(Convert.ToHexString(SHA256.HashData(image)));
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Puts an item on the system clipboard.</summary>
    public void Copy(HistoryItem item, bool removeFormatting = false)
    {
        Notifier.Notify(HistoryItem.ShortenTitle(item.SearchableText, false), NotifySound.Knock);
        CopyQuietly(item, removeFormatting);
    }

    /// <summary>Puts plain text on the clipboard (e.g. the search query or OCR text).</summary>
    public void CopyText(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        SetData(data);
    }

    public void CopyQuietly(HistoryItem item, bool removeFormatting)
    {
        var data = new DataObject();
        if (item.HasFiles && !removeFormatting)
        {
            var list = new StringCollection();
            list.AddRange(item.Files!.ToArray());
            data.SetFileDropList(list);
        }

        if (item.HasImage && !removeFormatting)
        {
            try
            {
                var bytes = File.ReadAllBytes(Path.Combine(HistoryStore.ImagesDirectory, item.ImageFile!));
                data.SetData("PNG", new MemoryStream(bytes));
                var decoder = new PngBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                data.SetImage(decoder.Frames[0]);
            }
            catch
            {
                // Image file is gone; fall back to whatever text there is.
            }
        }

        var text = item.Text;
        if (text == null && item.HasFiles) text = string.Join(Environment.NewLine, item.Files!);
        if (text != null) data.SetText(text, TextDataFormat.UnicodeText);

        if (!removeFormatting)
        {
            if (item.Rtf != null) data.SetData(DataFormats.Rtf, item.Rtf);
            if (item.Html != null) data.SetData(DataFormats.Html, item.Html);
        }

        SetData(data);
    }

    private static void SetData(DataObject data)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (ExternalException)
            {
                Thread.Sleep(30);
            }
        }
    }

    /// <summary>Returns focus to the window that was active before the popup and sends Ctrl+V.</summary>
    public static async Task PasteAsync(IntPtr targetWindow)
    {
        // Let the user release the modifiers used to choose the item (e.g. Alt from Alt+Enter),
        // otherwise the target app would receive Ctrl+Alt+V.
        for (var i = 0; i < 100 && AnyModifierDown(); i++)
            await Task.Delay(10);

        if (targetWindow != IntPtr.Zero && Native.IsWindow(targetWindow))
            Native.SetForegroundWindow(targetWindow);
        await Task.Delay(60);

        Native.SendKeys(
            (Native.VK_CONTROL, false),
            (Native.VK_V, false),
            (Native.VK_V, true),
            (Native.VK_CONTROL, true));
    }

    private static bool AnyModifierDown() =>
        Native.IsKeyDown(Native.VK_SHIFT) || Native.IsKeyDown(Native.VK_CONTROL) || Native.IsKeyDown(Native.VK_MENU) ||
        Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN);

    public static void ClearSystemClipboard()
    {
        try { Clipboard.Clear(); } catch { /* ignore */ }
    }
}
