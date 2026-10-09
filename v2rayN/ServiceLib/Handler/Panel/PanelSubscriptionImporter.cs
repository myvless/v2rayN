using ServiceLib.Models.Entities;
using ServiceLib.ViewModels;

namespace ServiceLib.Handler.Panel;

/// <summary>
/// 智飞云：面板订阅自动导入
/// 登录后清理旧订阅，只保留"面板订阅"一个分组
/// </summary>
public static class PanelSubscriptionImporter
{
    public static async Task<bool> ImportAsync(MainWindowViewModel viewModel)
    {
        try
        {
            Logging.SaveLog("PanelSubscriptionImporter", "开始导入面板订阅");

            // 获取订阅URL
            var subResult = await PanelApi.FetchSubscriptionUrlAsync();
            if (!subResult.Success)
            {
                Logging.SaveLog("PanelSubscriptionImporter", $"获取订阅URL失败: {subResult.Message}");
                return false;
            }
            var subUrl = subResult.Message;
            Logging.SaveLog("PanelSubscriptionImporter", $"订阅URL: {subUrl[..Math.Min(60, subUrl.Length)]}...");

            // 清理所有旧订阅
            await SQLiteHelper.Instance.DeleteAllAsync<SubItem>();
            Logging.SaveLog("PanelSubscriptionImporter", "已清理旧订阅");

            // 添加面板订阅
            var config = AppManager.Instance.Config;
            var subItem = new SubItem
            {
                Id = string.Empty,
                Url = subUrl,
                Remarks = "面板订阅",
                Enabled = true,
            };
            var result = await ConfigHandler.AddSubItem(config, subItem);
            if (result != 0)
            {
                Logging.SaveLog("PanelSubscriptionImporter", $"AddSubItem 失败，返回码: {result}");
                return false;
            }
            Logging.SaveLog("PanelSubscriptionImporter", "订阅已添加，开始更新节点");

            // 触发订阅更新（直接调用，不用反射）
            await viewModel.UpdateSubscriptionProcess("", false);

            Logging.SaveLog("PanelSubscriptionImporter", "面板订阅导入完成");
            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("PanelSubscriptionImporter", ex);
            return false;
        }
    }
}
