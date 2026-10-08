using ServiceLib.Models.Entities;

namespace ServiceLib.Handler.Panel;

/// <summary>
/// 智飞云：面板订阅自动导入
/// 登录后清理旧订阅，只保留"面板订阅"一个分组
/// </summary>
public static class PanelSubscriptionImporter
{
    public static async Task<bool> ImportAsync(object viewModel)
    {
        try
        {
            // 获取订阅URL
            var subResult = await PanelApi.FetchSubscriptionUrlAsync();
            if (!subResult.Success)
            {
                return false;
            }
            var subUrl = subResult.Message;

            // 清理所有旧订阅
            await SQLiteHelper.Instance.DeleteAllAsync<SubItem>();

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
                return false;
            }

            // 触发订阅更新（通过反射调用 ViewModel 的方法，避免直接依赖）
            var vmType = viewModel.GetType();
            var method = vmType.GetMethod("UpdateSubscriptionProcess",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (method != null)
            {
                var task = method.Invoke(viewModel, new object[] { "", false }) as Task;
                if (task != null)
                {
                    await task;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("PanelSubscriptionImporter", ex);
            return false;
        }
    }
}
