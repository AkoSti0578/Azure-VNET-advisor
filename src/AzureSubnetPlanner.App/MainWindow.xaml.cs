using System.ComponentModel;
using System.Windows;
using AzureSubnetPlanner.App.ViewModels;

namespace AzureSubnetPlanner.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnClosing(object? sender, CancelEventArgs e) => ((MainViewModel)DataContext).SaveSettings();
}
