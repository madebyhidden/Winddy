using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Winddy.Services;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string? DownloadUrl, string PageUrl);

/// <summary>
/// Sparkle-style updater backed by GitHub Releases: checks the latest release, downloads the
/// exe for this architecture, swaps it in place and restarts.
/// </summary>
public static class Updater
{
    /// <summary>GitHub "owner/repo" that publishes Winddy releases (see .github/workflows/build.yml).</summary>
    public const string Repository = "madebyhidden/Winddy";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly HttpClient Http = CreateClient();
    private static DispatcherTimer? _timer;

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    private static string AssetName => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "Winddy-arm64.exe" : "Winddy-x64.exe";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Winddy/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Returns the newest release if it is newer than the running version, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        AppSettings.Update(s => s.LastUpdateCheck = DateTime.Now);
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        if (version <= CurrentVersion) return null;

        string? url = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            var all = assets.EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString()))
                .ToList();
            url = all.FirstOrDefault(a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase)).Url
                  ?? all.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).Url;
        }

        return new UpdateInfo(
            version,
            tag,
            root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            url,
            root.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : $"https://github.com/{Repository}/releases");
    }

    /// <summary>Downloads the new exe, puts it in place of the running one and starts it.</summary>
    public static async Task InstallAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct = default)
    {
        if (info.DownloadUrl == null) throw new InvalidOperationException("Release has no Windows executable.");
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        var downloaded = current + ".new";
        var old = current + ".old";

        using (var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(downloaded);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress.Report((double)read / total);
            }
            if (total > 0 && read != total) throw new IOException("Incomplete download.");
        }

        // Windows lets a running exe be renamed but not overwritten.
        File.Move(current, old, true);
        try
        {
            File.Move(downloaded, current);
        }
        catch
        {
            File.Move(old, current, true);
            throw;
        }

        Process.Start(new ProcessStartInfo(current, "--updated") { UseShellExecute = false });
    }

    /// <summary>Removes the previous exe left behind by an update.</summary>
    public static void CleanupAfterUpdate()
    {
        var current = Environment.ProcessPath;
        if (current == null) return;
        foreach (var leftover in new[] { current + ".old", current + ".new" })
        {
            try { if (File.Exists(leftover)) File.Delete(leftover); } catch { /* still locked; next time */ }
        }
    }

    /// <summary>Checks shortly after launch and then once a day while "check automatically" is on.</summary>
    public static void StartAutomaticChecks(Action<UpdateInfo> onUpdateFound)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(1);
            var s = AppSettings.Current;
            if (!s.CheckForUpdates) return;
            if (s.LastUpdateCheck is { } last && DateTime.Now - last < CheckInterval) return;
            try
            {
                var info = await CheckAsync();
                if (info != null && info.Tag != AppSettings.Current.SkippedVersion) onUpdateFound(info);
            }
            catch
            {
                // Offline or rate-limited: try again later.
            }
        };
        _timer.Start();
    }
}
