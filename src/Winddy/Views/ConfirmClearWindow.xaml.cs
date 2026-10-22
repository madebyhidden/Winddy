using System.Windows;
using Winddy.Services;

namespace Winddy.Views;

public partial class ConfirmClearWindow : Window
{
    public ConfirmClearWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Theme.StyleWindow(this);
    }

    public bool DontAskAgain => DontAsk.IsChecked == true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
