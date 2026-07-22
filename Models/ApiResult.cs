namespace GithubManager.Models;

public class ApiResult
{
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string ErrorCode { get; set; } = "";
    public string Message { get; set; } = "";
    public string TechnicalDetail { get; set; } = "";
    public string Context { get; set; } = "";
    public string RequestUrl { get; set; } = "";
    public string ResponseBody { get; set; } = "";

    public static ApiResult Ok(string message = "ok") =>
        new() { Success = true, Message = message };

    public static ApiResult Fail(int? status, string code, string message, string? tech = null,
        string? ctx = null, string? url = null, string? body = null) =>
        new()
        {
            Success = false,
            StatusCode = status,
            ErrorCode = code,
            Message = message,
            TechnicalDetail = tech ?? "",
            Context = ctx ?? "",
            RequestUrl = url ?? "",
            ResponseBody = body ?? ""
        };

    public string HumanMessage()
    {
        var code = StatusCode.HasValue ? $"HTTP {StatusCode}" : ErrorCode;
        return $"{code}: {Message}";
    }
}
