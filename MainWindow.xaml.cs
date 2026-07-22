using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GithubManager.ViewModels;
using GithubManager.Views;

namespace GithubManager;

public partial class MainWindow : Window
{
    private bool _initialized;
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += MainWindow_StateChanged;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // 最大化时切换图标为还原图标
        if (WindowState == WindowState.Maximized)
        {
            MaximizeIcon.Data = Geometry.Parse("M 0 3 L 0 10 L 7 10 L 7 3 Z M 3 0 L 3 3 L 10 3 L 10 10 L 7 10 L 7 7 L 10 7 L 10 3 L 3 3 L 3 7 L 0 7 L 0 0 L 10 0 L 10 3 L 7 3");
        }
        else
        {
            MaximizeIcon.Data = Geometry.Parse("M 0 2 L 0 10 L 8 10 L 8 2 Z");
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainTabs_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_initialized)
        {
            AccountsFrame.Navigate(new AccountsPage());
            UploadFrame.Navigate(new UploadPage());
            FilesFrame.Navigate(new FilesPage());
            ReleaseFrame.Navigate(new ReleasePage());
            _initialized = true;
        }
    }

    private void SwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        var vm = App.AccountsVm;
        if (vm.SelectedAccount != null)
            vm.SwitchAccountCommand.Execute(vm.SelectedAccount);
    }

    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        var vm = App.AccountsVm;
        if (vm.SelectedAccount != null)
            vm.DeleteAccountCommand.Execute(vm.SelectedAccount);
    }
}
