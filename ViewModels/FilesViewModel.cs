using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubManager.Models;
using GithubManager.Services;
using Microsoft.Win32;

namespace GithubManager.ViewModels;

public partial class FilesViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    public FilesViewModel(MainViewModel main) => _main = main;

    [ObservableProperty]
    private ObservableCollection<RepositoryItem> _repos = new();

    [ObservableProperty]
    private RepositoryItem? _selectedRepo;

    [ObservableProperty]
    private ObservableCollection<BranchItem> _branches = new();

    [ObservableProperty]
    private BranchItem? _selectedBranch;

    [ObservableProperty]
    private ObservableCollection<ContentTreeItem> _rootItems = new();

    [ObservableProperty]
    private ContentTreeItem? _selectedItem;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _technicalDetail = "";

    [ObservableProperty]
    private string _repoFilter = "";

    public ObservableCollection<RepositoryItem> FilteredRepos =>
        string.IsNullOrWhiteSpace(RepoFilter)
            ? Repos
            : new ObservableCollection<RepositoryItem>(
                Repos.Where(r => r.FullName.Contains(RepoFilter,
                    StringComparison.OrdinalIgnoreCase)));

    private void ShowError(ApiResult res)
    {
        StatusMessage = res.HumanMessage();
        IsError = true;
        TechnicalDetail = $"URL: {res.RequestUrl}\nBody: {res.ResponseBody}\nTech: {res.TechnicalDetail}";
    }

    private void ShowOk(string msg)
    {
        StatusMessage = msg;
        IsError = false;
        TechnicalDetail = "";
    }

    [RelayCommand]
    private async Task RefreshRepos()
    {
        if (_main.CurrentAccount == null) return;
        IsBusy = true;
        try
        {
            var svc = _main.CreateReposService();
            var (res, list) = await svc.GetRepos();
            if (!res.Success) { ShowError(res); return; }
            Repos = new ObservableCollection<RepositoryItem>(list);
            OnPropertyChanged(nameof(FilteredRepos));
            ShowOk($"已加载 {list.Count} 个仓库");
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "获取仓库失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task LoadBranchesAsync()
    {
        if (SelectedRepo == null) return;
        IsBusy = true;
        try
        {
            var svc = _main.CreateReposService();
            var (res, list) = await svc.GetBranches(SelectedRepo.Owner, SelectedRepo.Name);
            if (!res.Success) { ShowError(res); return; }
            Branches = new ObservableCollection<BranchItem>(list);
            SelectedBranch = Branches.FirstOrDefault(b =>
                b.Name.Equals(SelectedRepo.DefaultBranch, StringComparison.OrdinalIgnoreCase))
                ?? Branches.FirstOrDefault();
            ShowOk($"已加载 {list.Count} 个分支");
            // 自动打开仓库根目录
            await LoadRootAsync();
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "获取分支失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task OpenRepoAsync()
    {
        if (SelectedRepo == null || SelectedBranch == null)
        {
            ShowError(ApiResult.Fail(null, "no_repo", "请先选择仓库和分支"));
            return;
        }
        await LoadRootAsync();
    }

    /// <summary>加载根目录内容</summary>
    private async Task LoadRootAsync()
    {
        if (SelectedRepo == null || SelectedBranch == null) return;
        IsBusy = true;
        try
        {
            var svc = _main.CreateContentsService();
            var (res, list) = await svc.GetDirectory(
                SelectedRepo.Owner, SelectedRepo.Name, "");
            if (!res.Success) { ShowError(res); return; }
            RootItems = new ObservableCollection<ContentTreeItem>(
                list.OrderByDescending(i => i.IsDir).ThenBy(i => i.Name)
                    .Select(c => ToTreeItem(c, null)));
            ShowOk($"/ ({RootItems.Count} 项)");
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "加载目录失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    /// <summary>展开文件夹时懒加载子节点</summary>
    public async Task LoadChildrenAsync(ContentTreeItem folder)
    {
        if (!folder.IsDirectory || folder.IsLoaded) return;
        if (SelectedRepo == null || SelectedBranch == null) return;
        IsBusy = true;
        try
        {
            var svc = _main.CreateContentsService();
            var (res, list) = await svc.GetDirectory(
                SelectedRepo.Owner, SelectedRepo.Name, folder.Path);
            if (!res.Success) { ShowError(res); return; }
            folder.Children.Clear();
            foreach (var c in list.OrderByDescending(i => i.IsDir).ThenBy(i => i.Name))
                folder.Children.Add(ToTreeItem(c, folder));
            folder.IsLoaded = true;
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "加载子目录失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    /// <summary>刷新整个树（从根开始重新加载）</summary>
    public async Task ReloadTreeAsync()
    {
        await LoadRootAsync();
    }

    private static ContentTreeItem ToTreeItem(ContentItem c, ContentTreeItem? parent)
    {
        var node = new ContentTreeItem
        {
            Name = c.Name,
            Path = c.Path,
            Type = c.Type,
            Size = c.Size,
            Sha = c.Sha,
            DownloadUrl = c.DownloadUrl,
            IsDirectory = c.IsDir,
            Parent = parent
        };
        // 目录加一个占位子节点，让 TreeView 显示展开箭头
        if (node.IsDirectory)
            node.Children.Add(new ContentTreeItem { Name = "..." });
        return node;
    }

    /// <summary>展开目录</summary>
    [RelayCommand]
    private async Task ToggleExpandAsync(ContentTreeItem? item)
    {
        if (item?.IsDirectory == true)
        {
            item.IsExpanded = !item.IsExpanded;
            if (item.IsExpanded)
                await LoadChildrenAsync(item);
        }
    }

    [RelayCommand]
    private async Task DownloadAsync(ContentTreeItem? item)
    {
        if (item == null) return;
        if (item.IsDirectory)
        {
            await DownloadFolderAsync(item);
            return;
        }
        // 单文件下载
        var dlg = new SaveFileDialog { FileName = item.Name };
        if (dlg.ShowDialog() != true) return;
        IsBusy = true;
        try
        {
            var svc = _main.CreateContentsService();
            var (res, _, content) = await svc.GetFile(
                SelectedRepo!.Owner, SelectedRepo.Name, item.Path);
            if (!res.Success) { ShowError(res); return; }
            await File.WriteAllTextAsync(dlg.FileName, content);
            ShowOk($"已保存到 {dlg.FileName}");
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "下载失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    /// <summary>递归下载整个文件夹</summary>
    private async Task DownloadFolderAsync(ContentTreeItem folder)
    {
        var dlg = new OpenFolderDialog
        {
            Title = $"选择保存位置（将创建 {folder.Name}/ 子文件夹）"
        };
        if (dlg.ShowDialog() != true) return;

        var targetDir = Path.Combine(dlg.FolderName, folder.Name);
        Directory.CreateDirectory(targetDir);

        // 先确保文件夹子节点已加载
        await LoadChildrenAsync(folder);

        IsBusy = true;
        try
        {
            var (ok, fail) = await DownloadTreeAsync(folder, targetDir);
            ShowOk($"文件夹下载完成：{ok} 成功，{fail} 失败 → {targetDir}");
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "下载文件夹失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    private async Task<(int ok, int fail)> DownloadTreeAsync(ContentTreeItem node, string localDir)
    {
        var svc = _main.CreateContentsService();
        await LoadChildrenAsync(node);
        var ok = 0; var fail = 0;

        foreach (var child in node.Children)
        {
            if (child.Name == "...") continue;
            var localPath = Path.Combine(localDir, child.Name);
            if (child.IsDirectory)
            {
                Directory.CreateDirectory(localPath);
                var (subOk, subFail) = await DownloadTreeAsync(child, localPath);
                ok += subOk; fail += subFail;
            }
            else
            {
                try
                {
                    var (res, _, content) = await svc.GetFile(
                        SelectedRepo!.Owner, SelectedRepo.Name, child.Path);
                    if (res.Success)
                    {
                        await File.WriteAllTextAsync(localPath, content ?? "");
                        ok++;
                    }
                    else fail++;
                }
                catch { fail++; }
            }
        }
        return (ok, fail);
    }

    [RelayCommand]
    private async Task EditAsync(ContentTreeItem? item)
    {
        if (item == null || item.IsDirectory) return;
        IsBusy = true;
        try
        {
            var svc = _main.CreateContentsService();
            var (res, info, content) = await svc.GetFile(
                SelectedRepo!.Owner, SelectedRepo.Name, item.Path);
            if (!res.Success) { ShowError(res); return; }

            var editVm = new EditFileViewModel
            {
                Owner = SelectedRepo.Owner,
                Repo = SelectedRepo.Name,
                Branch = SelectedBranch!.Name,
                Path = item.Path,
                OriginalContent = content,
                OriginalSha = info!.Sha
            };
            var win = new Views.EditWindow { DataContext = editVm };
            if (win.ShowDialog() == true)
            {
                await ReloadTreeAsync();
            }
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "打开编辑失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync(ContentTreeItem? item)
    {
        if (item == null) return;
        if (item.IsDirectory)
        {
            await DeleteFolderAsync(item);
            return;
        }

        // 单文件删除
        var msg = MessageBox.Show($"确定删除 {item.Name}？此操作会写入一个新的 commit。",
            "确认删除", MessageBoxButton.YesNo);
        if (msg != MessageBoxResult.Yes) return;

        var commitDlg = new Views.CommitMessageWindow();
        if (commitDlg.ShowDialog() != true) return;
        var commitMsg = commitDlg.CommitMessage;
        if (string.IsNullOrWhiteSpace(commitMsg)) commitMsg = $"delete {item.Name}";

        IsBusy = true;
        try
        {
            var svc = _main.CreateContentsService();
            var (res, info, _) = await svc.GetFile(
                SelectedRepo!.Owner, SelectedRepo.Name, item.Path);
            if (!res.Success) { ShowError(res); return; }

            var delRes = await svc.DeleteFile(SelectedRepo.Owner, SelectedRepo.Name,
                item.Path, commitMsg, SelectedBranch!.Name, info!.Sha);
            if (!delRes.Success) { ShowError(delRes); return; }
            ShowOk($"已删除 {item.Name}");
            await ReloadTreeAsync();
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "删除失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    /// <summary>递归删除整个文件夹（逐个删除所有子文件）</summary>
    private async Task DeleteFolderAsync(ContentTreeItem folder)
    {
        // 先收集所有文件路径
        var files = new System.Collections.Generic.List<ContentTreeItem>();
        await LoadChildrenAsync(folder);
        CollectFiles(folder, files);

        if (files.Count == 0)
        {
            // 空文件夹（GitHub 上没有真正的空文件夹），直接刷新树
            ShowOk("文件夹为空（GitHub 会在最后一个文件删除后自动移除目录）");
            await ReloadTreeAsync();
            return;
        }

        var confirm = MessageBox.Show(
            $"确定删除 {folder.Name}/ 及其全部 {files.Count} 个文件？\n此操作不可撤销！",
            "删除文件夹", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var commitDlg = new Views.CommitMessageWindow();
        if (commitDlg.ShowDialog() != true) return;
        var commitMsg = commitDlg.CommitMessage;
        if (string.IsNullOrWhiteSpace(commitMsg)) commitMsg = $"delete {folder.Path}";

        IsBusy = true;
        var ok = 0; var fail = 0;
        try
        {
            var svc = _main.CreateContentsService();
            foreach (var f in files.OrderByDescending(f => f.Path.Length)) // 先删深层文件
            {
                var (res, info, _) = await svc.GetFile(
                    SelectedRepo!.Owner, SelectedRepo.Name, f.Path);
                if (!res.Success || info == null) { fail++; continue; }

                var delRes = await svc.DeleteFile(SelectedRepo.Owner, SelectedRepo.Name,
                    f.Path, commitMsg, SelectedBranch!.Name, info.Sha);
                if (delRes.Success) ok++;
                else fail++;
            }
            ShowOk($"文件夹删除完成：{ok} 成功，{fail} 失败");
            await ReloadTreeAsync();
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "删除文件夹失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    /// <summary>递归收集文件夹内所有文件（叶子节点）</summary>
    private static void CollectFiles(ContentTreeItem node,
        System.Collections.Generic.List<ContentTreeItem> result)
    {
        foreach (var child in node.Children)
        {
            if (child.Name == "...") continue;
            if (child.IsDirectory)
                CollectFiles(child, result);
            else
                result.Add(child);
        }
    }

    [RelayCommand]
    private async Task DeleteRepoAsync()
    {
        if (SelectedRepo == null) return;
        if (MessageBox.Show($"⚠️ 确定永久删除仓库 {SelectedRepo.FullName}？\n此操作不可撤销！",
            "删除仓库", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            var svc = _main.CreateReposService();
            var res = await svc.DeleteRepo(SelectedRepo.Owner, SelectedRepo.Name);
            if (!res.Success) { ShowError(res); return; }
            ShowOk($"仓库 {SelectedRepo.FullName} 已删除");
            await RefreshReposAsync();
            RootItems = new();
        }
        catch (Exception ex)
        {
            ShowError(ApiResult.Fail(null, "unexpected", "删除仓库失败", ex.Message));
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshReposAsync()
    {
        if (_main.CurrentAccount == null) return;
        var svc = _main.CreateReposService();
        var (res, list) = await svc.GetRepos();
        if (res.Success)
        {
            Repos = new ObservableCollection<RepositoryItem>(list);
            OnPropertyChanged(nameof(FilteredRepos));
        }
    }
}

/// <summary>树形文件节点（支持懒加载）</summary>
public partial class ContentTreeItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _type = "";
    [ObservableProperty] private long _size;
    [ObservableProperty] private string _sha = "";
    [ObservableProperty] private string _downloadUrl = "";
    [ObservableProperty] private bool _isDirectory;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private ObservableCollection<ContentTreeItem> _children = new();

    public ContentTreeItem? Parent { get; set; }

    public string SizeText => IsDirectory ? ""
        : Size < 1024 ? $"{Size} B"
        : Size < 1024 * 1024 ? $"{Size / 1024.0:F1} KB"
        : $"{Size / 1024.0 / 1024.0:F1} MB";

    public string Icon => IsDirectory ? "📁" : "📄";

    // 让 TreeView 能展开目录（即使还没加载子节点，也显示占位展开箭头）
    public bool HasDummyChild => IsDirectory;
}

public partial class EditFileViewModel : ObservableObject
{
    [ObservableProperty] private string _owner = "";
    [ObservableProperty] private string _repo = "";
    [ObservableProperty] private string _branch = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _originalContent = "";
    [ObservableProperty] private string _editContent = "";
    [ObservableProperty] private string _originalSha = "";
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _technicalDetail = "";

    public async Task<bool> SubmitAsync(MainViewModel main)
    {
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            StatusMessage = "请填写 commit message";
            IsError = true;
            return false;
        }
        IsBusy = true;
        try
        {
            var svc = main.CreateContentsService();
            var (res, info, _) = await svc.GetFile(Owner, Repo, Path);
            if (!res.Success)
            {
                StatusMessage = res.HumanMessage();
                IsError = true;
                TechnicalDetail = res.TechnicalDetail;
                return false;
            }
            if (info!.Sha != OriginalSha)
            {
                StatusMessage = "冲突：远端文件已被他人修改，请关闭重试";
                IsError = true;
                return false;
            }
            var bytes = System.Text.Encoding.UTF8.GetBytes(EditContent);
            var base64 = Convert.ToBase64String(bytes);
            var upRes = await svc.UploadFile(Owner, Repo, Path, base64,
                CommitMessage, Branch, OriginalSha);
            if (!upRes.Success)
            {
                StatusMessage = upRes.HumanMessage();
                IsError = true;
                TechnicalDetail = upRes.TechnicalDetail;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"提交失败：{ex.Message}";
            IsError = true;
            return false;
        }
        finally { IsBusy = false; }
    }
}
