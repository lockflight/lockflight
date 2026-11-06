using System.Windows;
using Desk.Client.ViewModels;

namespace Desk.Client;

/// <summary>
/// The view: it binds the ViewModel and handles what only a window can do (focus). Behaviour lives in
/// <see cref="OrderViewModel"/>, where a test needs no window.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public OrderViewModel ViewModel { get; } = new();

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Clear();
        QuantityBox.Focus();
    }
}
