using System.ComponentModel;

namespace SWTools.ViewModel {
    public class MoreWindow : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 配置
        public Core.Config Config {
            get { return Core.ConfigManager.Config; }
            set {
                Core.ConfigManager.Config = value;
                OnPropertyChanged(nameof(Config));
            }
        }

        // 版本
        public string Version { get; set; } = Core.Constants.Version.ToString();
        public string PubVersion { get; set; } = Core.AccountManager.PubVersion;

        // 是否提醒过用户新版本
        public static bool HasHintedLatestVersion { get; set; } = false;

        // Steam 账号登录状态（供 UI 绑定）
        private Core.ELoginState _loginState = Core.ELoginState.NotLoggedIn;
        public Core.ELoginState LoginState {
            get => _loginState;
            set {
                if (_loginState == value) return;
                _loginState = value;
                OnPropertyChanged(nameof(LoginState));
                OnPropertyChanged(nameof(LoginStateText));
                OnPropertyChanged(nameof(IsLoginBtnEnabled));
            }
        }

        // 状态文字描述
        public string LoginStateText => LoginState switch {
            Core.ELoginState.NotLoggedIn => "未登录",
            Core.ELoginState.LoggingIn   => "登录中...",
            Core.ELoginState.LoggedIn    => $"已登录（{Core.SteamLoginService.LoggedInUsername}）",
            Core.ELoginState.Failed      => "登录失败",
            _                            => "未知"
        };

        // 登录/重新登录按钮是否可用（登录中时禁用）
        public bool IsLoginBtnEnabled => LoginState != Core.ELoginState.LoggingIn;

        // 同步当前实际登录状态（供代码隐藏调用）
        public void SyncLoginState() {
            LoginState = Core.SteamLoginService.State;
        }
    }
}
