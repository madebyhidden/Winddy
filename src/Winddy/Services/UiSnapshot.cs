using System;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Winddy.Models;
using Winddy.Views;

namespace Winddy.Services;

/// <summary>
/// Visual check used by CI (`Winddy.exe --ui-snapshot DIR`): fills a throwaway history with
/// sample items, opens every window in turn and saves real screen captures (including Mica/acrylic).
/// </summary>
public static class UiSnapshot
{
    public static async Task RunAsync(App app, string directory)
    {
        Directory.CreateDirectory(directory);
        Seed(app.Store);
        await Task.Delay(1500);

        AppSettings.Current.PopupPosition = PopupPosition.ScreenCenter;
        app.Popup.ShowPopup(fromTray: false);
        await Task.Delay(1500);
        CaptureScreen(Path.Combine(directory, "01-popup-screen.png"));
        Capture(app.Popup, Path.Combine(directory, "02-popup.png"), 0);
        app.Popup.OpenPreviewForSnapshot();
        await Task.Delay(800);
        CaptureScreen(Path.Combine(directory, "03-popup-preview.png"));
        app.Popup.HidePopup();

        app.ShowSettings();
        await Task.Delay(1500);
        var settings = FindWindow<SettingsWindow>();
        if (settings != null)
        {
            for (var page = 0; page < settings.NavList.Items.Count; page++)
            {
                settings.NavList.SelectedIndex = page;
                await Task.Delay(700);
                Capture(settings, Path.Combine(directory, $"1{page}-settings-{page}.png"));
            }
            settings.Close();
        }

        app.ShowAbout();
        await Task.Delay(1000);
        if (FindWindow<AboutWindow>() is { } about) { Capture(about, Path.Combine(directory, "20-about.png")); about.Close(); }

        var confirm = new ConfirmClearWindow();
        confirm.Show();
        await Task.Delay(1000);
        Capture(confirm, Path.Combine(directory, "21-confirm-clear.png"));
        confirm.Close();

        var update = new UpdateWindow(app, new UpdateInfo(new Version(1, 1, 0), "v1.1.0",
            "• Fluent design\n• Faster search\n• Bug fixes", "https://example.invalid/Winddy-x64.exe", "https://github.com/" + Updater.Repository));
        update.Show();
        await Task.Delay(1000);
        Capture(update, Path.Combine(directory, "22-update.png"));
        update.Close();

        var editor = new ItemEditorWindow(app.Store, app.Store.Sorted()[0]);
        editor.Show();
        await Task.Delay(1000);
        Capture(editor, Path.Combine(directory, "23-editor.png"));
        editor.Close();

        app.Tray?.OpenMenuForSnapshot();
        await Task.Delay(800);
        CaptureScreen(Path.Combine(directory, "24-tray-menu-screen.png"));

        app.Quit();
    }

    private static T? FindWindow<T>() where T : Window
    {
        foreach (Window w in Application.Current.Windows)
            if (w is T t) return t;
        return null;
    }

    private static void Seed(HistoryStore store)
    {
        string[] samples =
        {
            "git commit -m \"Fix clipboard listener\"",
            "https://github.com/madebyhidden/Winddy",
            "#2563EB",
            "Привет! Это пример текста в истории буфера обмена.",
            "public static void Main(string[] args)\n{\n    Console.WriteLine(\"Hello\");\n}",
            "  indented text with trailing spaces   ",
            "support@example.com",
            "SELECT * FROM users WHERE id = 42;",
            "Meeting notes: ship v1.1 on Friday",
            "C:\\Users\\Public\\Documents\\report.pdf",
            "42",
        };
        var time = DateTime.Now.AddMinutes(-samples.Length);
        foreach (var text in samples)
        {
            time = time.AddMinutes(1);
            store.Add(new HistoryItem
            {
                Text = text,
                Application = @"C:\Windows\explorer.exe",
                FirstCopiedAt = time,
                LastCopiedAt = time,
                Hash = ClipboardService.ComputeHash(text, null, null),
            });
        }
        var sorted = store.Sorted();
        store.TogglePin(sorted[^1]);
        store.TogglePin(sorted[^2]);
    }

    private static void Capture(Window window, string path, int margin = 0)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        Native.GetWindowRect(hwnd, out var r);
        CaptureRect(r.Left - margin, r.Top - margin, r.Width + margin * 2, r.Height + margin * 2, path);
    }

    private static void CaptureScreen(string path)
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        CaptureRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height, path);
    }

    private static void CaptureRect(int x, int y, int width, int height, string path)
    {
        if (width <= 0 || height <= 0) return;
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
            g.CopyFromScreen(x, y, 0, 0, bitmap.Size);
        bitmap.Save(path, ImageFormat.Png);
    }
}
