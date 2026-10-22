using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using Winddy.Services;

namespace Winddy.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Theme.StyleWindow(this);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = Loc.T("Version", version == null ? "1.0" : $"{version.Major}.{version.Minor}.{version.Build}");
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Links.Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
