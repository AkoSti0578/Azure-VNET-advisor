using System.Windows;
using System.Windows.Threading;

namespace AzureSubnetPlanner.App;

public partial class App : Application
{
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}",
            "Azure Subnet Planner",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
