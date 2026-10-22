using System.Windows;
using Wpf.Ui.Controls;
using Winddy.Services;

namespace Winddy.Views;

public partial class AboutWindow : FluentWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = Loc.T("Version", Updater.CurrentVersion.ToString(3));
    }

    private void Repo_Click(object sender, RoutedEventArgs e) => Links.Open($"https://github.com/{Updater.Repository}");

    private void Maccy_Click(object sender, RoutedEventArgs e) => Links.Open("https://github.com/p0deje/Maccy");
}
