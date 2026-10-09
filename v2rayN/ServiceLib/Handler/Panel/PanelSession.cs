namespace ServiceLib.Handler.Panel;

/// <summary>
/// 面板登录态管理（内存态，App 重启后需重新登录）
/// </summary>
public static class PanelSession
{
    public static bool IsLoggedIn { get; private set; }
    public static string? LoggedInEmail { get; private set; }

    public static void MarkLoggedIn(string email)
    {
        IsLoggedIn = true;
        LoggedInEmail = email;
    }

    public static void Logout()
    {
        IsLoggedIn = false;
        LoggedInEmail = null;
        PanelApi.ClearSession();
    }
}
