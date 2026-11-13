using System;
using System.Windows;
namespace NEO.Views;
public partial class NotchWindow : Window
{
    public event Action? PanelRequested;
    public NotchWindow() => InitializeComponent();
    private void OpenPanel(object sender, RoutedEventArgs e) => PanelRequested?.Invoke();
}
