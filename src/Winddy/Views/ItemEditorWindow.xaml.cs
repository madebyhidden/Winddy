using System.Linq;
using System.Windows;
using System.Windows.Input;
using Winddy.Models;
using Winddy.Services;

namespace Winddy.Views;

/// <summary>Edits an item's text, and for pinned items also its title and key.</summary>
public partial class ItemEditorWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly HistoryStore _store;
    private readonly HistoryItem _item;

    public ItemEditorWindow(HistoryStore store, HistoryItem item)
    {
        _store = store;
        _item = item;
        InitializeComponent();

        PinFields.Visibility = item.IsPinned ? Visibility.Visible : Visibility.Collapsed;
        if (item.IsPinned)
        {
            TitleBox.Text = item.Title ?? "";
            KeyBox.ItemsSource = store.AvailablePins(item).Append(item.Pin!).Distinct().OrderBy(p => p).Select(p => p.ToUpperInvariant()).ToList();
            KeyBox.SelectedItem = item.Pin!.ToUpperInvariant();
        }

        if (item.IsPlainTextOnly)
        {
            ContentBox.Text = item.Text ?? "";
            WarningText.Visibility = item.Rtf != null || item.Html != null ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ContentBox.Text = Loc.T("ContentIsNotText");
            ContentBox.IsEnabled = false;
        }

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Save();
                e.Handled = true;
            }
        };
        Loaded += (_, _) => (ContentBox.IsEnabled ? ContentBox : TitleBox).Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (_item.IsPinned)
        {
            _store.UpdateTitle(_item, TitleBox.Text);
            if (KeyBox.SelectedItem is string key) _store.ChangePin(_item, key.ToLowerInvariant());
        }
        if (_item.IsPlainTextOnly) _store.UpdateText(_item, ContentBox.Text);
        DialogResult = true;
    }
}
