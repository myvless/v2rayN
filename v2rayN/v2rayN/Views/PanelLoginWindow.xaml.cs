using System.Diagnostics;
using System.Windows;
using ServiceLib.Handler.Panel;

namespace v2rayN.Views;

/// <summary>
/// 智飞云面板登录窗口
/// </summary>
public partial class PanelLoginWindow : Window
{
    public bool LoginSucceeded { get; private set; }

    public PanelLoginWindow()
    {
        InitializeComponent();
        txtEmail.Focus();
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        var email = txtEmail.Text.Trim();
        var password = txtPassword.Password;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            txtError.Text = "请输入邮箱和密码";
            return;
        }

        btnLogin.IsEnabled = false;
        btnLogin.Content = "登录中...";
        txtError.Text = "";

        try
        {
            var result = await PanelApi.LoginAsync(email, password);
            if (result.Success)
            {
                PanelSession.MarkLoggedIn(email);
                LoginSucceeded = true;
                DialogResult = true;
                Close();
            }
            else
            {
                txtError.Text = result.Message;
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"登录失败: {ex.Message}";
        }
        finally
        {
            btnLogin.IsEnabled = true;
            btnLogin.Content = "登录";
        }
    }

    private void LinkRegister_Click(object sender, RoutedEventArgs e)
    {
        // 在浏览器中打开注册页面
        Process.Start(new ProcessStartInfo
        {
            FileName = PanelConfig.PanelBaseUrl + PanelConfig.PathRegister,
            UseShellExecute = true,
        });
    }
}
