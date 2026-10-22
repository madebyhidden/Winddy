using System.Windows;
using Wpf.Ui.Controls;

namespace Winddy.Views;

public partial class ConfirmClearWindow : FluentWindow
{
    public ConfirmClearWindow()
    {
        InitializeComponent();
    }

    public bool DontAskAgain => DontAsk.IsChecked == true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
