using System.Windows;
using System.Windows.Input;

namespace SWTools.WPF {
    /// <summary>
    /// Steam Guard 令牌输入对话框
    /// </summary>
    public partial class SteamGuardDialog : Window {
        // 用户输入的令牌（确认后设置）
        public string? GuardCode { get; private set; }

        public SteamGuardDialog() {
            InitializeComponent();
            Loaded += (_, _) => TxtCode.Focus();
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
