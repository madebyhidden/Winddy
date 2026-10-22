using System;
using System.Windows;
using Winddy.Services;

namespace Winddy.Views;

public partial class UpdateWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly App _app;
    private readonly UpdateInfo _info;

    public UpdateWindow(App app, UpdateInfo info)
    {
        _app = app;
        _info = info;
        InitializeComponent();
        VersionsText.Text = Loc.T("UpdateVersions", info.Version.ToString(3), Updater.CurrentVersion.ToString(3));
        NotesText.Text = string.IsNullOrWhiteSpace(info.Notes) ? "—" : info.Notes.Trim();
        if (info.DownloadUrl == null) InstallButton.Content = Loc.T("OpenReleasePage");
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Update(s => s.SkippedVersion = _info.Tag);
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_info.DownloadUrl == null)
        {
            Links.Open(_info.PageUrl);
            Close();
            return;
        }

        InstallButton.IsEnabled = SkipButton.IsEnabled = LaterButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;
        Progress.Visibility = Visibility.Visible;
        try
        {
            await Updater.InstallAsync(_info, new Progress<double>(p => Progress.Value = p));
            _app.Quit();
        }
        catch (Exception ex)
        {
            // Typically no write access to the install folder: fall back to the release page.
            ErrorText.Text = Loc.T("UpdateFailed", ex.Message);
            ErrorText.Visibility = Visibility.Visible;
            Progress.Visibility = Visibility.Collapsed;
            InstallButton.Content = Loc.T("OpenReleasePage");
            InstallButton.IsEnabled = SkipButton.IsEnabled = LaterButton.IsEnabled = true;
            InstallButton.Click -= Install_Click;
            InstallButton.Click += (_, _) => { Links.Open(_info.PageUrl); Close(); };
        }
    }
}
