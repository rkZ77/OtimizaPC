using System.Windows;
using System.Windows.Controls;
using Fpsx.App.ViewModels;

namespace Fpsx.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) =>
        {
            await _vm.StartAsync();
            // Primeira coisa ao abrir: diagnóstico (só leitura). O fluxo da seção 46
            // é detectar e analisar antes de oferecer qualquer alteração.
            if (_vm.Pages[0] is DashboardViewModel dash && dash.ScanCommand.CanExecute(null))
                dash.ScanCommand.Execute(null);
        };
    }

    private void NavItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.DataContext == _vm.Current)
            rb.IsChecked = true;
    }
}
