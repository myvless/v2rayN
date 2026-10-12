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
    /// 新版面板渲染 {$UniversalSub} 即 https://panel.020178.xyz/sub/{token}，
    /// V2Ray 格式为 …/sub/{token}/v2ray；旧版面板用 /link/{token}?sub=1
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

            string? url = null;
            // 1. 优先 V2Ray 格式（v2rayN 需要 vmess:// 链接）：…/sub/{token}/v2ray
            var match = Regex.Match(html, @"https?://[^""'<>\s""]+/sub/[^""'<>\s""/]+/v2ray(?=[""'\s>])");
            if (match.Success)
            {
                url = match.Value;
            }
            else
            {
                // 2. 退回通用订阅地址 …/sub/{token}，v2rayN 直接取其 v2ray 子格式
                match = Regex.Match(html, @"https?://[^""'<>\s""]+/sub/[^""'<>\s""/]+(?=[""'\s>])");
                if (match.Success)
                {
                    url = match.Value + "/v2ray";
                }
                else
                {
                    // 3. 兼容旧版面板 /link/{token}
                    match = Regex.Match(html, @"https?://[^""'<>\s]+/link/[^""'<>\s?]+(?:\?[^""'<>\s]*)?");
                    if (match.Success)
                    {
                        url = match.Value;
                        // 旧版 /link/ 地址需要 ?sub=1 参数；新版 /sub/ 地址格式已在路径中
                        if (!url.Contains("sub="))
                        {
                            url += url.Contains("?") ? "&sub=1" : "?sub=1";
                        }
                    }
                }
            }

            if (url != null)
            {
                Logging.SaveLog($"PanelApi: 订阅URL: {url[..Math.Min(60, url.Length)]}...");
                return new ApiResult(true, url);
            }
            Logging.SaveLog("PanelApi: 未在 /user 页面找到订阅链接");
            return new ApiResult(false, "未找到订阅链接，请确认账号有效");
        }
        catch (Exception ex)
        {
            return new ApiResult(false, $"网络错误：{ex.Message}");
        }
    }

    /// <summary>
    /// 获取账户信息（流量、到期时间）
    /// 优先从订阅响应的 Subscription-Userinfo 头解析，失败回退解析 /user 页面
    /// </summary>
    public static async Task<PanelUserInfo> FetchAccountInfoAsync()
    {
        // 先尝试从订阅的 Subscription-Userinfo 头获取（最可靠）
        try
        {
            var subResult = await FetchSubscriptionUrlAsync();
            if (subResult.Success)
            {
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
                        long.TryParse(totalStr, out var total) && total > 0)
                    {
                        var upload = dict.TryGetValue("upload", out var u) && long.TryParse(u, out var uv) ? uv : 0;
                        var download = dict.TryGetValue("download", out var d) && long.TryParse(d, out var dv) ? dv : 0;
                        var used = upload + download;
                        var percent = (float)used / total;
                        if (percent < 0) percent = 0;
                        if (percent > 1) percent = 1;

                        var expireDate = string.Empty;
                        if (dict.TryGetValue("expire", out var expireStr) &&
                            long.TryParse(expireStr, out var expireTs) && expireTs > 0)
                        {
                            expireDate = DateTimeOffset.FromUnixTimeSeconds(expireTs).LocalDateTime.ToString("yyyy-MM-dd");
                        }

                        return new PanelUserInfo(
                            PanelSession.LoggedInEmail ?? string.Empty,
                            expireDate,
                            FormatBytes(used),
                            FormatBytes(total),
                            percent);
                    }
                }
            }
        }
        catch { }

        // 回退：解析 /user 页面 HTML
        try
        {
            var resp = await _client.GetAsync(PanelConfig.PanelBaseUrl + PanelConfig.PathUser);
            if (!resp.IsSuccessStatusCode)
            {
                return new PanelUserInfo(PanelSession.LoggedInEmail ?? string.Empty);
            }
            var html = await resp.Content.ReadAsStringAsync();

            var used = string.Empty;
            var total = string.Empty;
            var percent = 0f;
            // 新版模板：过去用量 X / 今日用量 Y / 剩余流量 Z（分开显示，无 X/Y 形式）
            var mUsed = Regex.Match(html, @"过去用量[^0-9]*([\d.]+\s*[KMGT]?B)");
            var mRemain = Regex.Match(html, @"剩余流量[^0-9]*([\d.]+\s*[KMGT]?B)");
            if (mUsed.Success && mRemain.Success)
            {
                used = mUsed.Groups[1].Value;
                var usedBytes = ParseBytes(used);
                var remainBytes = ParseBytes(mRemain.Groups[1].Value);
                var totalBytes = usedBytes + remainBytes;
                if (totalBytes > 0)
                {
                    total = FormatBytes(totalBytes);
                    percent = (float)usedBytes / totalBytes;
                    if (percent < 0) percent = 0;
                    if (percent > 1) percent = 1;
                }
            }
            else
            {
                // 兼容旧版 X / Y 形式
                var m = Regex.Match(html, @"([\d.]+\s*[KMGT]?B)\s*/\s*([\d.]+\s*[KMGT]?B)");
                if (m.Success)
                {
                    used = m.Groups[1].Value;
                    total = m.Groups[2].Value;
                }
                m = Regex.Match(html, @"(\d+(?:\.\d+)?)\s*%");
                if (m.Success && float.TryParse(m.Groups[1].Value, out var p))
                {
                    percent = p / 100f;
                    if (percent < 0) percent = 0;
                    if (percent > 1) percent = 1;
                }
            }
            var expire = string.Empty;
            var m = Regex.Match(html, @"到期[^<]{0,30}?(\d{4}-\d{2}-\d{2})");
            if (m.Success)
            {
                expire = m.Groups[1].Value;
            }
            return new PanelUserInfo(PanelSession.LoggedInEmail ?? string.Empty, expire, used, total, percent);
        }
        catch
        {
            return new PanelUserInfo(PanelSession.LoggedInEmail ?? string.Empty);
        }
    }

    private static long ParseBytes(string text)
    {
        // 解析 "1.5 GB" / "300 MB" / "0 B" 为字节数
        var m = Regex.Match(text.Trim(), @"([\d.]+)\s*([KMGT]?)B");
        if (!m.Success || !double.TryParse(m.Groups[1].Value, out var num))
        {
            return 0;
        }
        var mult = m.Groups[2].Value switch
        {
            "K" => 1024L,
            "M" => 1024L * 1024,
            "G" => 1024L * 1024 * 1024,
            "T" => 1024L * 1024 * 1024 * 1024,
            _ => 1L,
        };
        return (long)(num * mult);
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
