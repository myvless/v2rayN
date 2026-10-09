namespace ServiceLib.Handler.Panel;

/// <summary>
/// 智飞云：面板账户信息（仿安卓 v8 PanelUserInfo）
/// </summary>
public record PanelUserInfo(
    string Email = "",
    string ExpireDate = "",
    string TrafficUsed = "",
    string TrafficTotal = "",
    float TrafficPercent = 0f
);
