using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Winddy.Services;

public static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Winddy";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(ValueName, false);
        }
    }
}

public static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;

        ImageSource? result = null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                result = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
            }
        }
        catch
        {
            // Inaccessible executable.
        }
        Cache[path] = result;
        return result;
    }
}

public static class Theme
{
    /// <summary>True when Windows apps are set to the dark theme.</summary>
    public static bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Switches the Fluent resources to the system light/dark theme and accent colour.</summary>
    public static void Apply()
    {
        var theme = IsDark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.Mica, true);
    }

    /// <summary>Windows 11 22H2+ can draw a system acrylic backdrop behind a window.</summary>
    public static bool SupportsBackdrop => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Asks Windows 11 for rounded corners and a dark frame where applicable; with <paramref name="acrylic"/>
    /// the window gets the translucent material Windows uses for flyouts (Maccy's vibrancy counterpart).
    /// </summary>
    public static void StyleWindow(Window window, bool acrylic = false)
    {
        if (acrylic && SupportsBackdrop)
        {
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
            if (chrome != null) chrome.GlassFrameThickness = new Thickness(-1);
            window.Background = System.Windows.Media.Brushes.Transparent;
            window.BorderThickness = new Thickness(0);
        }

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var round = 2; // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        var dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));

        if (acrylic && SupportsBackdrop)
        {
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
            var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            var backdrop = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
            Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
        }
    }

    /// <summary>Re-applies the dark/light frame after the system theme changes.</summary>
    public static void RefreshFrame(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }
}

public static class Links
{
    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* ignore */ }
    }
}
