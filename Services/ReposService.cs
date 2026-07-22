using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubManager.Models;

namespace GithubManager.Services;

public class ReposService
{
    private readonly GitHubClient _client;
    public ReposService(GitHubClient client) => _client = client;

    public async Task<(ApiResult result, Account? account)> ValidateToken(
        string login, CancellationToken ct = default)
    {
        var (res, data) = await _client.GetAsync<JsonElement>(
            "https://api.github.com/user", ct);
        if (!res.Success) return (res, null);

        var acc = new Account
        {
            Login = data.GetProperty("login").GetString() ?? login,
            AvatarUrl = data.TryGetProperty("avatar_url", out var av) ?
                av.GetString() ?? "" : ""
        };
        acc.TokenTarget = $"GithubManager:{acc.Login}";
        return (ApiResult.Ok(), acc);
    }

    public async Task<(ApiResult result, List<RepositoryItem> repos)> GetRepos(
        CancellationToken ct = default)
    {
        var all = new List<RepositoryItem>();
        var url = "https://api.github.com/user/repos?per_page=100&sort=updated" +
                  "&affiliation=owner,collaborator,organization_member";
        var (res, data) = await _client.GetAsync<JsonElement>(url, ct);
        if (!res.Success)
            return (ApiResult.Fail(res.StatusCode, res.ErrorCode,
                $"获取仓库列表失败（{res.Message}）", res.TechnicalDetail,
                "GetRepos", url, res.ResponseBody), all);

        foreach (var el in data.EnumerateArray())
        {
            var owner = el.GetProperty("owner").GetProperty("login").GetString() ?? "";
            el.TryGetProperty("name", out var nameEl);
            el.TryGetProperty("full_name", out var fnEl);
            el.TryGetProperty("description", out var descEl);
            el.TryGetProperty("private", out var privEl);
            el.TryGetProperty("fork", out var forkEl);
            el.TryGetProperty("default_branch", out var defEl);
            all.Add(new RepositoryItem
            {
                Name = nameEl.GetString() ?? "",
                FullName = fnEl.GetString() ?? "",
                Owner = owner,
                Description = descEl.GetString() ?? "",
                IsPrivate = privEl.ValueKind == JsonValueKind.True,
                IsFork = forkEl.ValueKind == JsonValueKind.True,
                DefaultBranch = defEl.GetString() ?? "main"
            });
        }
        return (ApiResult.Ok(), all.OrderBy(r => r.FullName).ToList());
    }

    public async Task<(ApiResult result, List<BranchItem> branches)> GetBranches(
        string owner, string repo, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/branches?per_page=100";
        var (res, data) = await _client.GetAsync<JsonElement>(url, ct);
        if (!res.Success)
            return (ApiResult.Fail(res.StatusCode, res.ErrorCode,
                $"获取分支列表失败（{res.Message}）", res.TechnicalDetail,
                $"{owner}/{repo}", url, res.ResponseBody), new());

        var list = new List<BranchItem>();
        foreach (var el in data.EnumerateArray())
        {
            el.TryGetProperty("name", out var n);
            list.Add(new BranchItem { Name = n.GetString() ?? "" });
        }
        return (ApiResult.Ok(), list);
    }

    public async Task<(ApiResult result, RepositoryItem? repo)> CreateRepo(
        string name, string description, bool isPrivate, bool autoInit,
        CancellationToken ct = default)
    {
        var body = new
        {
            name,
            description,
            _private = isPrivate,
            auto_init = autoInit
        };
        var (res, data) = await _client.PostAsync<JsonElement>(
            "https://api.github.com/user/repos", body, ct);
        if (!res.Success)
            return (ApiResult.Fail(res.StatusCode, res.ErrorCode,
                $"创建仓库失败（{res.Message}）", res.TechnicalDetail,
                name, "https://api.github.com/user/repos", res.ResponseBody), null);

        var owner = data.GetProperty("owner").GetProperty("login").GetString() ?? "";
        data.TryGetProperty("name", out var nameEl);
        data.TryGetProperty("full_name", out var fnEl);
        data.TryGetProperty("default_branch", out var defEl);
        return (ApiResult.Ok(), new RepositoryItem
        {
            Name = nameEl.GetString() ?? name,
            FullName = fnEl.GetString() ?? $"{owner}/{name}",
            Owner = owner,
            Description = description,
            IsPrivate = isPrivate,
            DefaultBranch = defEl.GetString() ?? "main"
        });
    }

    public async Task<ApiResult> DeleteRepo(string owner, string repo,
        CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}";
        var res = await _client.DeleteAsync(url, ct);
        if (!res.Success)
            return ApiResult.Fail(res.StatusCode, res.ErrorCode,
                $"删除仓库失败（{res.Message}）", res.TechnicalDetail,
                $"{owner}/{repo}", url, res.ResponseBody);
        return ApiResult.Ok();
    }
}
