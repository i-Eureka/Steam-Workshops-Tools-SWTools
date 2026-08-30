using Semver;
using System.IO;
using System.Windows;
using System.Windows.Navigation;

namespace SWTools.WPF {
    /// <summary>
    /// MoreWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MoreWindow : Window {
        // ViewModel 访问点
        public ViewModel.MoreWindow ViewModel {
            get => (ViewModel.MoreWindow)DataContext;
            set { DataContext = value; }
        }

        public MoreWindow() {
            InitializeComponent();
            LicenseText.Text = Helper.GetEmbeddedResource("SWTools.WPF.LICENSE.txt");
            MdViewer.Content = Helper.GetEmbeddedResource("SWTools.WPF.THIRD-PARTY-NOTICE.md");
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            MdViewer.Dispose();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e) {
            Close();
        }

        private void BtnGithub_Click(object sender, RoutedEventArgs e) {
            System.Diagnostics.Process.Start("explorer.exe",
                Core.Constants.UrlRepo);
        }

        private void BtnClearCache_Click(object sender, RoutedEventArgs e) {
            MsgBox msgBox0 = new("操作确认", "确认要清空缓存吗？\n", true) { Owner = this };
            if (msgBox0.ShowDialog() == true) {
                Core.Helper.Main.ClearAllCache();
                MsgBox msgBox = new("清理完成", "已删除缓存（程序正在引用的缓存除外）。", false) { Owner = this };
                msgBox.ShowDialog();
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e) {
            MsgBox msgBox0 = new("操作确认", "确认要重置所有设置吗？\n", true) { Owner = this };
            if (msgBox0.ShowDialog() == true) {
                ViewModel.Config = new();
            }
        }

        private void BtnOpenDownloadFolder_Click(object sender, RoutedEventArgs e) {
            var path = Core.Constants.SteamcmdDir + "steamapps/workshop/content/";
            System.Diagnostics.Process.Start("explorer.exe",
                Path.GetFullPath(path));
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
            System.Diagnostics.Process.Start("explorer.exe", e.Uri.ToString());
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            // 同步登录状态
            ViewModel.SyncLoginState();

            // 若记住密码，则预填 PasswordBox（PasswordBox 不支持双向绑定）
            if (Core.ConfigManager.Config.CustomRememberPassword &&
                !string.IsNullOrEmpty(Core.ConfigManager.Config.CustomPassword)) {
                PbPassword.Password = Core.ConfigManager.Config.CustomPassword;
            }

            // 提示新版本
            if (!SWTools.ViewModel.MoreWindow.HasHintedLatestVersion) {
                SWTools.ViewModel.MoreWindow.HasHintedLatestVersion = true;
                var info = Core.Helper.Main.ReadLatestInfo();
                if (info == null) return;
                if (info.Release != null &&
                    SemVersion.Parse(info.Release).CompareSortOrderTo(Core.Constants.Version) > 0) {
                    MsgBox msgBox = new("发现新版本", $"检测到新的发行版：{info.Release}\n（当前版本：{Core.Constants.Version}）\n\n" +
                        $"您可以在下方链接获取新版本。", false,
                        "查看 Release 页面", Core.Constants.UrlRelease) { Owner = this };
                    msgBox.ShowDialog();
                } else if (info.PreRelease != null &&
                    SemVersion.Parse(info.PreRelease).CompareSortOrderTo(Core.Constants.Version) > 0) {
                    MsgBox msgBox = new("发现新的预览版本", $"检测到新的预发行版：{info.Release}\n（当前版本：{Core.Constants.Version}）\n\n" +
                        $"您可以在下方链接获取新版本。", false,
                        "查看 Release 页面", Core.Constants.UrlRelease) { Owner = this };
                    msgBox.ShowDialog();
                }
            }
        }

        private void BtnUninstallSteamcmd_Click(object sender, RoutedEventArgs e) {
            MsgBox msgBox = new("操作确认", "确定要卸载 Steamcmd 吗？卸载后可能导致下面的后果：\n\n" +
                "1. 如果您有存放在 Steamcmd 目录下的物品，这些物品将会被删除。您可以点击 “打开总下载目录” 来确认；\n" +
                "2. 下次下载物品时，将重新安装 Steamcmd，下载用时将增加。", true) { Owner = this };
            bool? res = msgBox.ShowDialog();
            if (res == true) {
                try {
                    Directory.Delete(Core.Constants.SteamcmdDir, true);
                    Core.LogManager.Log.Information("Successfully deleted steamcmd");
                    msgBox = new("操作成功", $"成功卸载了 Steamcmd。", false) { Owner = this };
                    msgBox.ShowDialog();
                } catch (Exception ex) {
                    Core.LogManager.Log.Error("Failed to delete steamcmd: {Exception}", ex);
                    msgBox = new("操作失败", $"无法删除 Steamcmd 所在文件夹。您可以尝试自行删除程序目录下的 {Core.Constants.SteamcmdDir} 目录，" +
                        $"或检查程序日志。", false) { Owner = this };
                    msgBox.ShowDialog();
                }
            }
        }

        // PasswordBox 内容变更时同步（PasswordBox 安全性不支持绑定）
        private void PbPassword_PasswordChanged(object sender, RoutedEventArgs e) {
            if (Core.ConfigManager.Config.CustomRememberPassword) {
                Core.ConfigManager.Config.CustomPassword = PbPassword.Password;
            }
        }

        // 取消"记住密码"时，立即清除持久化的密码
        private void ChkRememberPassword_Unchecked(object sender, RoutedEventArgs e) {
            Core.ConfigManager.Config.CustomPassword = string.Empty;
            Core.ConfigManager.Save("ClearPassword");
        }

        // 登录 / 重新登录
        private async void BtnLogin_Click(object sender, RoutedEventArgs e) {
            var username = TxtUsername.Text.Trim();
            var password = PbPassword.Password;

            if (string.IsNullOrWhiteSpace(username)) {
                MsgBox msg = new("输入错误", "请填写用户名。", false) { Owner = this };
                msg.ShowDialog();
                return;
            }
            if (string.IsNullOrEmpty(password)) {
                MsgBox msg = new("输入错误", "请填写密码。", false) { Owner = this };
                msg.ShowDialog();
                return;
            }

            // 保存用户名；密码仅在"记住密码"开启时保存
            Core.ConfigManager.Config.CustomUsername = username;
            if (Core.ConfigManager.Config.CustomRememberPassword) {
                Core.ConfigManager.Config.CustomPassword = password;
            } else {
                Core.ConfigManager.Config.CustomPassword = string.Empty;
            }
            Core.ConfigManager.Save("Login");

            // 立即更新 UI 为"正在登录"状态，禁用登录按钮
            ViewModel.LoginState = Core.ELoginState.LoggingIn;

            // 启动登录（异步）
            var result = await Core.SteamLoginService.LoginAsync(
                username,
                password,
                getGuardCode: async () => {
                    // 需要在 UI 线程弹出对话框
                    string? code = null;
                    await Dispatcher.InvokeAsync(() => {
                        var dlg = new SteamGuardDialog();
                        // 只有当前窗口仍有效时才设置 Owner（避免已关闭时崩溃）
                        if (IsLoaded && IsVisible) {
                            dlg.Owner = this;
                        }
                        if (dlg.ShowDialog() == true) {
                            code = dlg.GuardCode;
                        }
                    });
                    return code;
                });

            // 同步最终状态
            ViewModel.SyncLoginState();

            // 若窗口已关闭则不弹结果对话框
            if (!IsLoaded || !IsVisible) return;

            // 提示结果
            string resultMsg = result switch {
                Core.ELoginResult.Success        => "登录成功！后续下载将优先使用此账号。",
                Core.ELoginResult.InvalidPassword => "账号或密码错误，请检查后重试。",
                Core.ELoginResult.GuardCodeFailed => "Steam Guard 令牌错误或已过期，请重新登录并输入最新令牌。",
                Core.ELoginResult.NetworkError    => "网络连接失败，请检查网络后重试。",
                Core.ELoginResult.Cancelled       => "已取消登录（未输入令牌）。",
                _                                 => "登录失败（未知原因），请查看日志获取详情。"
            };
            MsgBox resultBox = new(
                result == Core.ELoginResult.Success ? "登录成功" : "登录失败",
                resultMsg,
                false);
            if (IsLoaded && IsVisible) resultBox.Owner = this;
            resultBox.ShowDialog();
        }

        // 重置登录状态（不清除 Steamcmd 缓存）
        private void BtnResetLoginState_Click(object sender, RoutedEventArgs e) {
            Core.SteamLoginService.ResetState();
            ViewModel.SyncLoginState();
            MsgBox msg = new("已重置", "登录状态已重置为“未登录”。\n（Steamcmd 本地会话缓存未被清除）", false) { Owner = this };
            msg.ShowDialog();
        }
    }
}
