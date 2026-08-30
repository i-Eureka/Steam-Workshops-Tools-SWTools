using System.Windows;
using System.Windows.Input;

namespace SWTools.WPF {
    /// <summary>
    /// Steam Guard 令牌输入对话框。
    /// isAppConfirm=false：要求用户输入 TOTP / 邮箱验证码。
    /// isAppConfirm=true ：提示用户在 Steam 手机 App 上点击确认，无输入框。
    /// </summary>
    public partial class SteamGuardDialog : Window {
        // 用户输入的令牌（确认后设置，App 确认模式下为 null）
        public string? GuardCode { get; private set; }

        public SteamGuardDialog(bool isAppConfirm = false) {
            InitializeComponent();
            if (isAppConfirm) {
                TxtPrompt.Text = "Steam 需要通过 Steam 手机 App 确认登录。\n\n请打开 Steam 手机 App，在通知或\"确认\"页面中批准此次登录请求，然后点击\"完成\"。";
                TxtCode.Visibility = Visibility.Collapsed;
                BtnOk.Content = "完成";
                // App 确认模式：直接点"完成"即可（告知用户已在手机操作）
                BtnOk.Click -= BtnOk_Click;
                BtnOk.Click += (_, _) => { DialogResult = true; };
            } else {
                Loaded += (_, _) => TxtCode.Focus();
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e) {
            var code = TxtCode.Text.Trim();
            if (string.IsNullOrEmpty(code)) {
                TxtCode.Focus();
                return;
            }
            GuardCode = code;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
        }

        private void TxtCode_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) BtnOk_Click(sender, new RoutedEventArgs());
        }
    }
}
