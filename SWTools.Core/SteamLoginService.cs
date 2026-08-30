using Serilog;
using System.Diagnostics;
using System.Text;

namespace SWTools.Core {
    /// <summary>
    /// Steam 账号登录状态
    /// </summary>
    public enum ELoginState {
        NotLoggedIn,    // 未登录
        LoggingIn,      // 登录中
        LoggedIn,       // 已登录
        Failed          // 登录失败
    }

    /// <summary>
    /// Steam 登录操作结果
    /// </summary>
    public enum ELoginResult {
        Success,            // 成功
        InvalidPassword,    // 账号或密码错误
        NeedGuardCode,      // 需要 Steam Guard 令牌
        GuardCodeFailed,    // 令牌错误或已过期
        NetworkError,       // 网络失败
        Cancelled,          // 用户取消（获取令牌时）
        Unknown             // 未知原因
    }

    /// <summary>
    /// Steam 账号登录服务（静态类）
    /// 负责通过 Steamcmd 完成用户名+密码登录，支持 Steam Guard（App 令牌）二次验证。
    /// 登录成功后 Steamcmd 会在本地缓存会话，后续下载可复用。
    /// </summary>
    public static class SteamLoginService {
        // 当前登录状态
        public static ELoginState State { get; private set; } = ELoginState.NotLoggedIn;
        // 已登录的用户名（登录成功后设置）
        public static string LoggedInUsername { get; private set; } = string.Empty;

        /// <summary>
        /// 执行登录流程：用户名 + 密码，若需要 Steam Guard，则通过回调获取令牌。
        /// </summary>
        /// <param name="username">Steam 用户名</param>
        /// <param name="password">Steam 密码</param>
        /// <param name="getGuardCode">
        ///   当需要 Steam Guard 令牌时调用的异步回调，返回用户输入的令牌；
        ///   返回 null 或空字符串表示用户取消。
        /// </param>
        public static async Task<ELoginResult> LoginAsync(
            string username,
            string password,
            Func<Task<string?>> getGuardCode) {

            State = ELoginState.LoggingIn;
            LoggedInUsername = string.Empty;

            try {
                // 第一次尝试：仅用户名 + 密码
                var result = await TryLoginAsync(username, password, null);

                if (result == ELoginResult.NeedGuardCode) {
                    // 需要令牌：通过回调向用户请求
                    var code = await getGuardCode();
                    if (string.IsNullOrWhiteSpace(code)) {
                        State = ELoginState.NotLoggedIn;
                        return ELoginResult.Cancelled;
                    }
                    // 第二次尝试：附带令牌
                    result = await TryLoginAsync(username, password, code.Trim());
                }

                if (result == ELoginResult.Success) {
                    State = ELoginState.LoggedIn;
                    LoggedInUsername = username;
                } else {
                    State = ELoginState.Failed;
                }
                return result;
            } catch (Exception ex) {
                LogManager.Log.Error("Exception occurred during Steam login:\n{Exception}", ex);
                State = ELoginState.Failed;
                return ELoginResult.Unknown;
            }
        }

        /// <summary>
        /// 单次 Steamcmd 登录尝试。
        /// guardCode 为 null 时执行首次登录（检测是否需要 2FA）；非 null 时携带令牌登录。
        /// 关闭 stdin 以防止 Steamcmd 在等待 2FA 输入时永久挂起。
        /// </summary>
        private static async Task<ELoginResult> TryLoginAsync(
            string username,
            string password,
            string? guardCode) {

            if (!File.Exists(Constants.SteamcmdFile)) {
                LogManager.Log.Error("Steamcmd not found at \"{Path}\"", Constants.SteamcmdFile);
                return ELoginResult.Unknown;
            }

            // 构造参数：+login username password [guardcode] +quit
            // 注意：凭据通过命令行参数传递，在进程列表中可见。
            // 后续可改为通过 Steamcmd 的 stdin 管道传入以降低暴露风险；
            // 与现有 Item.Core.cs 的下载逻辑保持一致（均为命令行参数方式）。
            string loginArgs = guardCode != null
                ? $"+login {username} {password} {guardCode} +quit"
                : $"+login {username} {password} +quit";

            ProcessStartInfo startInfo = Helper.Steamcmd.GetProcessStartInfo(loginArgs);
            using Process process = Process.Start(startInfo)
                ?? throw new Exception("Failed to start steamcmd process");

            // 关闭 stdin，防止在需要 2FA 时进程永久等待输入
            process.StandardInput.Close();

            // 同时读取 stdout 和 stderr，避免因缓冲区满导致死锁
            // （ProcessStartInfo 中 RedirectStandardError = true）
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask  = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = await outputTask;
            await errorTask; // 确保 stderr 被完全消费（防止死锁）

            LogManager.Log.Debug("Steamcmd login output:\n{Output}", output);

            return ParseLoginOutput(output);
        }

        /// <summary>
        /// 解析 Steamcmd 登录输出，返回对应结果。
        /// </summary>
        private static ELoginResult ParseLoginOutput(string output) {
            // 成功
            if (output.Contains("Logged in OK"))
                return ELoginResult.Success;

            // 令牌错误
            if (output.Contains("Invalid login auth code") ||
                output.Contains("Two-factor code mismatch") ||
                output.Contains("incorrect Steam Guard code"))
                return ELoginResult.GuardCodeFailed;

            // 需要 Steam Guard 令牌（首次登录时 steamcmd 会提示）
            // 关闭 stdin 后 steamcmd 通常输出 "Two-factor code:" 后因 EOF 退出
            if (output.Contains("Two-factor code") ||
                output.Contains("Steam Guard") ||
                output.Contains("two-factor"))
                return ELoginResult.NeedGuardCode;

            // 密码错误
            if (output.Contains("Invalid Password") ||
                output.Contains("incorrect password"))
                return ELoginResult.InvalidPassword;

            // 网络错误
            if (output.Contains("Network unreachable") ||
                output.Contains("Failed to connect") ||
                output.Contains("Connection failed") ||
                output.Contains("Timeout"))
                return ELoginResult.NetworkError;

            LogManager.Log.Warning("Could not parse steamcmd login output");
            return ELoginResult.Unknown;
        }

        /// <summary>
        /// 重置登录状态（不清除 Steamcmd 缓存的会话）。
        /// </summary>
        public static void ResetState() {
            State = ELoginState.NotLoggedIn;
            LoggedInUsername = string.Empty;
        }
    }
}
