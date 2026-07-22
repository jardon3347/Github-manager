using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GithubManager.Models;

namespace GithubManager.Services;

public class ContentsService
{
    private readonly GitHubClient _client;
    public ContentsService(GitHubClient client) => _client = client;

    public async Task<(ApiResult result, List<ContentItem> items)> GetDirectory(
        string owner, string repo, string path, CancellationToken ct = default)
    {
        var encodedPath = string.IsNullOrEmpty(path) ? "" : Uri.EscapeDataString(path);
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{encodedPath}";
        var (res, data) = await _client.GetAsync<JsonElement>(url, ct);
        if (!res.Success)
            return (ApiResult.Fail(res.StatusCode, res.ErrorCode,
                $"获取目录失败（{res.Message}）", res.TechnicalDetail,
                $"{owner}/{repo}:{path}", url, res.ResponseBody), new());

        var list = new List<ContentItem>();
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in data.EnumerateArray())
                list.Add(ParseContent(el));
        }
        else
        {
            list.Add(ParseContent(data));
        }
        return (ApiResult.Ok(), list);
    }

    public async Task<(ApiResult result, ContentItem? item, string content)> GetFile(
        string owner, string repo, string path, CancellationToken ct = default)
    {
        var encodedPath = Uri.EscapeDataString(path);
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{encodedPath}";
        var (res, data) = await _client.GetAsync<JsonElement>(url, ct);
        if (!res.Success)
            return (ApiResult.Fail(res.StatusCode, res.ErrorCode,
                $"获取文件失败（{res.Message}）", res.TechnicalDetail,
                $"{owner}/{repo}:{path}", url, res.ResponseBody), null, "");

        var item = ParseContent(data);
        var raw = data.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
        var encoding = data.TryGetProperty("encoding", out var e) ? e.GetString() ?? "" : "";
        var decoded = "";
        if (encoding.Equals("base64", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(raw))
        {
            try
            {
                var bytes = Convert.FromBase64String(raw.Replace("\n", "").Replace("\r", ""));
                decoded = Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                decoded = raw;
            }
        }
        else decoded = raw;
        return (ApiResult.Ok(), item, decoded);
    }

    public async Task<ApiResult> UploadFile(
        string owner, string repo, string path, string contentBase64,
        string message, string branch, string? sha, CancellationToken ct = default)
    {
        var encodedPath = Uri.EscapeDataString(path);
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{encodedPath}";
        var body = new
        {
            message,
            content = contentBase64,
            branch,
            sha
        };
        var (res, _) = await _client.PutAsync<JsonElement>(url, body, ct);
        if (!res.Success)
            return ApiResult.Fail(res.StatusCode, res.ErrorCode,
                $"上传文件失败（{res.Message}）", res.TechnicalDetail,
                $"{owner}/{repo}:{path}", url, res.ResponseBody);
        return ApiResult.Ok();
    }

    public async Task<ApiResult> DeleteFile(
        string owner, string repo, string path, string message,
        string branch, string sha, CancellationToken ct = default)
    {
        var encodedPath = Uri.EscapeDataString(path);
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{encodedPath}";
        var body = new
        {
            message,
            sha,
            branch
        };
        var res = await _client.DeleteAsync(url, body, ct);
        if (!res.Success)
            return ApiResult.Fail(res.StatusCode, res.ErrorCode,
                $"删除文件失败（{res.Message}）", res.TechnicalDetail,
                $"{owner}/{repo}:{path}", url, res.ResponseBody);
        return ApiResult.Ok();
    }

    private static ContentItem ParseContent(JsonElement el)
    {
        el.TryGetProperty("name", out var nameEl);
        el.TryGetProperty("path", out var pathEl);
        el.TryGetProperty("type", out var typeEl);
        el.TryGetProperty("size", out var sizeEl);
        el.TryGetProperty("sha", out var shaEl);
        el.TryGetProperty("download_url", out var dlEl);
        return new ContentItem
        {
            Name = nameEl.GetString() ?? "",
            Path = pathEl.GetString() ?? "",
            Type = typeEl.GetString() ?? "",
            Size = sizeEl.ValueKind == JsonValueKind.Number ? sizeEl.GetInt64() : 0,
            Sha = shaEl.GetString() ?? "",
            DownloadUrl = dlEl.GetString() ?? ""
        };
    }
}
