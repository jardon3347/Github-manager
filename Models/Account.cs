namespace GithubManager.Models;

public class Account
{
    public string Login { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string TokenTarget { get; set; } = ""; // Windows 凭据管理器 key = GithubManager:<login>
}
