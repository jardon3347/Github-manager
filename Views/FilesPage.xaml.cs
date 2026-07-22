using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GithubManager.ViewModels;

namespace GithubManager.Views;

public partial class FilesPage : Page
{
    public FilesPage()
    {
        InitializeComponent();
        // 监听 TreeViewItem 展开事件，触发懒加载
        AddHandler(TreeViewItem.ExpandedEvent,
            new RoutedEventHandler(TreeViewItem_Expanded));
    }

    private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FilesViewModel vm) return;
        if (e.OriginalSource is TreeViewItem tvi &&
            tvi.DataContext is ContentTreeItem item && item.IsDirectory)
        {
            await vm.LoadChildrenAsync(item);
        }
    }

    private void Repo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is FilesViewModel vm)
            vm.LoadBranchesCommand.Execute(null);
    }

    private void TreeView_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is FilesViewModel vm)
            vm.SelectedItem = e.NewValue as ContentTreeItem;
    }

    private void TreeView_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not FilesViewModel vm) return;

        var source = e.OriginalSource as DependencyObject;
        var treeItem = FindTreeViewItem(source);
        if (treeItem?.DataContext is ContentTreeItem item && item.IsDirectory)
        {
            vm.ToggleExpandCommand.Execute(item);
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FilesViewModel vm)
            vm.DownloadCommand.Execute(vm.SelectedItem);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FilesViewModel vm)
            vm.EditCommand.Execute(vm.SelectedItem);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FilesViewModel vm)
            vm.DeleteCommand.Execute(vm.SelectedItem);
    }

    private void ShowTechDetail_Click(object sender, RoutedEventArgs e)
    {
        TechDetailBox.Visibility = TechDetailBox.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is TreeViewItem tvi) return tvi;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}
