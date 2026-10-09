using System.Windows;
using ServiceLib.Handler.Panel;

namespace v2rayN.Views;

/// <summary>
/// 智飞云：我的账户窗口（仿安卓 v8）
/// 显示邮箱、到期时间、流量使用情况
/// </summary>
public partial class PanelAccountWindow : Window
{
    public PanelAccountWindow()
    {
        InitializeComponent();
        txtEmail.Text = PanelSession.LoggedInEmail ?? "已登录";
        _ = LoadAccountInfoAsync();
    }

    private async Task LoadAccountInfoAsync()
    {
        txtLoading.Visibility = Visibility.Visible;
        panelInfo.Visibility = Visibility.Collapsed;
        try
        {
            var info = await PanelApi.FetchAccountInfoAsync();
            if (!string.IsNullOrEmpty(info.ExpireDate))
            {
                txtExpire.Text = $"到期时间：{info.ExpireDate}";
            }
            else
            {
                txtExpire.Text = "到期时间：--";
            }

            if (!string.IsNullOrEmpty(info.TrafficTotal))
            {
                txtTraffic.Text = $"流量：{info.TrafficUsed} / {info.TrafficTotal}";
                barTraffic.Value = info.TrafficPercent * 100;
            }
            else
            {
                txtTraffic.Text = "暂无流量信息";
                barTraffic.Value = 0;
            }
        }
        catch
        {
            txtTraffic.Text = "获取失败，请重试";
        }
        finally
        {
            txtLoading.Visibility = Visibility.Collapsed;
            panelInfo.Visibility = Visibility.Visible;
        }
    }

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("确定要退出登录吗？", "智飞云", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            PanelSession.Logout();
            DialogResult = true;
            Close();
        }
    }
}
