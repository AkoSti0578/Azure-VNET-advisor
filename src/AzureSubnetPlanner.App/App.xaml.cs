using System.Windows;
using System.Windows.Threading;

namespace AzureSubnetPlanner.App;

public partial class App : Application
{
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Er is een onverwachte fout opgetreden:\n\n{e.Exception.Message}",
            "Azure Subnet Planner",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
