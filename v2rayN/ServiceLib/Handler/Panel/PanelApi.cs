using System.Net.Http;

namespace ServiceLib.Handler.Panel;

/// <summary>
/// 机场面板 API 客户端
/// 直接调用面板 Web 接口（SSPanel-Uim），session 机制：
/// 登录/注册成功后服务端 Set-Cookie，后续请求自动携带。
/// </summary>
public static class PanelApi
{
    private static readonly CookieContainer _cookieContainer = new();
    private static readonly HttpClient _client;

    static PanelApi()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        _client.DefaultRequestHeaders.Add("User-Agent", "v2rayN-Panel/1.0");
    }

    public record ApiResult(bool Success, string Message);

    /// <summary>
    /// 登录面板
    /// POST /auth/login，成功时返回 HX-Redirect 头
    /// </summary>
    public static async Task<ApiResult> LoginAsync(string email, string password)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = email.Trim().ToLowerInvariant(),
                ["password"] = password,
                ["remember_me"] = "true",
            });
            var resp = await _client.PostAsync(PanelConfig.PanelBaseUrl + PanelConfig.PathLogin, content);
            var body = await resp.Content.ReadAsStringAsync();

            // 成功时服务端返回 HX-Redirect 头
            if (resp.Headers.TryGetValues("HX-Redirect", out _))
            {
                return new ApiResult(true, string.Empty);
            }

            var msg = ExtractMsg(body, "登录失败");
            return new ApiResult(false, msg);
        }
        catch (Exception ex)
        {
            return new ApiResult(false, $"网络错误：{ex.Message}");
        }
    }

    /// <summary>
    /// 注册面板账号
    /// POST /auth/register，成功时返回 HX-Redirect 头
    /// </summary>
    public static async Task<ApiResult> RegisterAsync(string email, string password, string inviteCode = "")
    {
        try
        {
            var dict = new Dictionary<string, string>
            {
                ["email"] = email.Trim().ToLowerInvariant(),
                ["name"] = email.Split('@')[0],
                ["password"] = password,
                ["confirm_password"] = password,
                ["tos"] = "true",
            };
            if (!string.IsNullOrWhiteSpace(inviteCode))
            {
                dict["invite_code"] = inviteCode.Trim();
            }
            var content = new FormUrlEncodedContent(dict);
            var resp = await _client.PostAsync(PanelConfig.PanelBaseUrl + PanelConfig.PathRegister, content);
            var body = await resp.Content.ReadAsStringAsync();

            if (resp.Headers.TryGetValues("HX-Redirect", out _))
            {
                return new ApiResult(true, string.Empty);
            }

            var msg = ExtractMsg(body, "注册失败");
            return new ApiResult(false, msg);
        }
        catch (Exception ex)
        {
            return new ApiResult(false, $"网络错误：{ex.Message}");
        }
    }

    /// <summary>
    /// 获取当前账号的订阅链接
    /// GET /user（需登录态），从页面 HTML 中解析订阅地址
    /// </summary>
    public static async Task<ApiResult> FetchSubscriptionUrlAsync()
    {
        try
        {
            var resp = await _client.GetAsync(PanelConfig.PanelBaseUrl + PanelConfig.PathUser);
            if (!resp.IsSuccessStatusCode)
            {
                return new ApiResult(false, $"获取订阅失败 ({(int)resp.StatusCode})，请重新登录");
            }
            var html = await resp.Content.ReadAsStringAsync();

            // 面板用户中心页面内嵌订阅链接，形如 https://panel.020178.xyz/link/xxx?sub=1
            var match = Regex.Match(html, @"https?://[^""'<>\s]+/link/[^""'<>\s?]+(?:\?[^""'<>\s]*)?");
            if (match.Success)
            {
                var url = match.Value;
                if (!url.Contains("sub="))
                {
                    url += url.Contains("?") ? "&sub=1" : "?sub=1";
                }
                return new ApiResult(true, url);
            }
            return new ApiResult(false, "未找到订阅链接，请确认账号有效");
        }
        catch (Exception ex)
        {
            return new ApiResult(false, $"网络错误：{ex.Message}");
        }
    }

    /// <summary>
    /// 获取账户信息（流量、到期时间）
    /// 优先从订阅响应的 Subscription-Userinfo 头解析
    /// </summary>
    public static async Task<string?> FetchAccountInfoAsync()
    {
        try
        {
            var subResult = await FetchSubscriptionUrlAsync();
            if (!subResult.Success)
            {
                return null;
            }

            var resp = await _client.GetAsync(subResult.Message);
            if (resp.Headers.TryGetValues("Subscription-Userinfo", out var values))
            {
                var header = string.Join(";", values);
                // 解析 upload=xxx; download=xxx; total=xxx; expire=xxx
                var dict = new Dictionary<string, string>();
                foreach (var part in header.Split(';'))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        dict[kv[0].Trim()] = kv[1].Trim();
                    }
                }

                if (dict.TryGetValue("total", out var totalStr) &&
                    dict.TryGetValue("upload", out var uploadStr) &&
                    dict.TryGetValue("download", out var downloadStr))
                {
                    var total = long.Parse(totalStr);
                    var used = long.Parse(uploadStr) + long.Parse(downloadStr);
                    var remaining = total - used;

                    var info = $"剩余流量: {FormatBytes(remaining)} / {FormatBytes(total)}";
                    if (dict.TryGetValue("expire", out var expireStr) && long.Parse(expireStr) > 0)
                    {
                        var expire = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expireStr)).LocalDateTime;
                        info += $"\n到期时间: {expire:yyyy-MM-dd}";
                    }
                    return info;
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F1} {units[unit]}";
    }

    /// <summary>
    /// 检查登录态是否有效（访问 /user 不被重定向到登录页）
    /// </summary>
    public static async Task<bool> CheckSessionAsync()
    {
        try
        {
            var resp = await _client.GetAsync(PanelConfig.PanelBaseUrl + PanelConfig.PathUser);
            var finalUrl = resp.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
            // 如果被重定向到登录页，说明 session 失效
            return resp.IsSuccessStatusCode && !finalUrl.Contains("/auth/login");
        }
        catch
        {
            return false;
        }
    }

    public static void ClearSession()
    {
        // 清空该域名的 cookies
        var uri = new Uri(PanelConfig.PanelBaseUrl);
        var cookies = _cookieContainer.GetCookies(uri);
        foreach (Cookie c in cookies)
        {
            c.Expired = true;
        }
    }

    private static string ExtractMsg(string body, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("msg", out var msgProp))
            {
                var msg = msgProp.GetString();
                if (!string.IsNullOrWhiteSpace(msg)) return msg;
            }
        }
        catch { }
        return fallback;
    }
}
