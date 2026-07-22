using System.IO;
using System;
using System.Net;
using System.Net.Http;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GithubManager.Models;

namespace GithubManager.Services;

public class GitHubClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public GitHubClient(string token)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("GithubManager", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<(ApiResult result, T? data)> GetAsync<T>(string url,
        CancellationToken ct = default)
    {
        return await RequestAsync<T>(HttpMethod.Get, url, null, ct);
    }

    public async Task<ApiResult> GetRawAsync(string url, CancellationToken ct = default)
    {
        return (await RequestAsync<object>(HttpMethod.Get, url, null, ct)).result;
    }

    public async Task<(ApiResult result, T? data)> PostAsync<T>(string url, object body,
        CancellationToken ct = default)
    {
        return await RequestAsync<T>(HttpMethod.Post, url, body, ct);
    }

    public async Task<(ApiResult result, T? data)> PutAsync<T>(string url, object body,
        CancellationToken ct = default)
    {
        return await RequestAsync<T>(HttpMethod.Put, url, body, ct);
    }

    public async Task<ApiResult> DeleteAsync(string url, object body, CancellationToken ct = default)
    {
        return (await RequestAsync<object>(HttpMethod.Delete, url, body, ct)).result;
    }

    /// <summary>DELETE 请求（无 body）</summary>
    public async Task<ApiResult> DeleteAsync(string url, CancellationToken ct = default)
    {
        return (await RequestAsync<object>(HttpMethod.Delete, url, null, ct)).result;
    }

    public async Task<ApiResult> UploadBinaryAsync(string url, Stream stream,
        string fileName, IProgress<long>? progress, CancellationToken ct = default)
    {
        try
        {
            using var content = new ProgressableStreamContent(stream, progress);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("form-data")
                {
                    Name = "file",
                    FileName = fileName
                };

            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = content;
            var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return ApiResult.Fail((int)resp.StatusCode, "upload_failed",
                    TranslateStatus(resp.StatusCode, body), body, fileName, url, body);
            return ApiResult.Ok();
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return ApiResult.Fail(null, "timeout", "请求超时，请检查网络连接", ex.Message, fileName, url);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult.Fail(null, "network_error", "网络不可达，请检查连接",
                ex.Message, fileName, url);
        }
    }

    private async Task<(ApiResult result, T? data)> RequestAsync<T>(HttpMethod method,
        string url, object? body, CancellationToken ct)
    {
        try
        {
            var req = new HttpRequestMessage(method, url);
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, JsonOpts);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var resp = await _http.SendAsync(req, ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                return (ApiResult.Fail((int)resp.StatusCode, "api_error",
                    TranslateStatus(resp.StatusCode, respBody),
                    respBody, null, url, respBody), default);
            }

            if (typeof(T) == typeof(object)) return (ApiResult.Ok(), default);

            try
            {
                var data = JsonSerializer.Deserialize<T>(respBody, JsonOpts);
                return (ApiResult.Ok(), data);
            }
            catch (JsonException ex)
            {
                return (ApiResult.Fail(null, "parse_error", "响应解析失败",
                    ex.Message, null, url, respBody), default);
            }
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return (ApiResult.Fail(null, "timeout", "请求超时，请检查网络连接",
                ex.Message, null, url), default);
        }
        catch (HttpRequestException ex)
        {
            return (ApiResult.Fail(null, "network_error", "网络不可达，请检查连接",
                ex.Message, null, url), default);
        }
    }

    private static string TranslateStatus(HttpStatusCode code, string body)
    {
        return code switch
        {
            HttpStatusCode.Unauthorized => "Token 已过期或无效，请重新登录",
            HttpStatusCode.Forbidden => "权限不足，请检查 PAT 的 scopes",
            HttpStatusCode.NotFound => "资源不存在（仓库/路径/Release 未找到）",
            (HttpStatusCode)422 => "请求参数校验失败（可能文件已被他人修改，或 tag 已存在）",
            HttpStatusCode.TooManyRequests => "触发 GitHub 限流，请稍后再尝试",
            HttpStatusCode.InternalServerError => "GitHub 服务暂时不可用",
            _ => $"请求失败：{(int)code} {code}"
        };
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>带进度上报的 StreamContent</summary>
public class ProgressableStreamContent : StreamContent
{
    private readonly Stream _stream;
    private readonly IProgress<long>? _progress;
    public ProgressableStreamContent(Stream stream, IProgress<long>? progress)
        : base(stream)
    {
        _stream = stream;
        _progress = progress;
    }

    protected override async Task SerializeToStreamAsync(Stream stream,
        [AllowNull] TransportContext context)
    {
        var buffer = new byte[81920];
        var total = _stream.Length;
        var uploaded = 0L;
        // 重置源流位置
        if (_stream.CanSeek) _stream.Position = 0;
        while (true)
        {
            var n = await _stream.ReadAsync(buffer);
            if (n == 0) break;
            await stream.WriteAsync(buffer.AsMemory(0, n));
            uploaded += n;
            _progress?.Report(total > 0 ? uploaded * 100 / total : 0);
        }
    }
}
