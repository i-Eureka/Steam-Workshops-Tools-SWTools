using PropertyChanged;
using System.ComponentModel;
using System.Text.Json;

namespace SWTools.Core {
    /// <summary>
    /// 可自定义的配置
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class Config : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        // 空配置
        public static readonly Config Empty = new();

        // [常规]
        public bool IgnoreMissingFiles { get; set; } = false;   // 忽略丢失的文件
        public bool NoAutoFetch { get; set; } = false;          // 禁用自动更新

        // [Steam 账号]
        public string CustomUsername { get; set; } = "";        // 自定义账号用户名
        public string CustomPassword { get; set; } = "";        // 自定义账号密码（明文存储，后续可接入 DPAPI 加密）
        public bool CustomRememberPassword { get; set; } = false; // 是否记住密码
        public string CustomGuardCode { get; set; } = "";       // Steam Guard 验证码（可选，明文存储）
        public bool CustomRememberGuardCode { get; set; } = false; // 是否记住验证码

        // [调试选项]
#if DEBUG
        public bool LogDebug { get; set; } = true;      // 输出调试日志
#else
        public bool LogDebug { get; set; } = false;     // 输出调试日志
#endif
        public bool NoCache { get; set; } = false;      // 禁用缓存

        // 序列化到 Json
        public override string ToString() {
            try {
                return JsonSerializer.Serialize(this, Constants.JsonOptions);
            }
            catch (Exception ex) {
                LogManager.Log.Error("Exception occurred when serializing Json:\n{Exception}", ex);
                return string.Empty;
            }
        }
    }
}
