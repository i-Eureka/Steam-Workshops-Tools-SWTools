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
        GuardCodeFailed,    // 令牌错误或已过期
        NetworkError,       // 网络失败
        Cancelled,          // 用户取消（未输入令牌或关闭对话框）
        Unknown             // 未知原因
    }

    /// <summary>
    /// Steam 账号登录服务（静态类）。
    /// 通过 Steamcmd stdin/stdout 交互完成登录，支持：
    ///   1. TOTP/邮箱验证码（Two-factor code:）：弹窗让用户输入
    ///   2. Steam App 确认（Use the Steam Mobile App to confirm your sign in）：提示用户在手机上操作，等待进程完成
    /// </summary>
    public static class SteamLoginService {
        // 当前登录状态
        public static ELoginState State { get; private set; } = ELoginState.NotLoggedIn;
        // 已登录的用户名（登录成功后设置）
        public static string LoggedInUsername { get; private set; } = string.Empty;

        // steamcmd 等待 App 确认时的超时（秒）
        private const int AppConfirmTimeoutSeconds = 120;

        /// <summary>
        /// 执行登录流程：用户名 + 密码，通过 stdin/stdout 与 steamcmd 交互。
        /// 若需要 TOTP，通过 getGuardCode 回调获取验证码后写入 stdin；
        /// 若需要 App 确认，通过 onAppConfirmPending 回调通知 UI，然后等待手机端完成确认。
        /// onLogLine 回调用于实时输出 steamcmd 日志（可选）。
        /// </summary>
        public static async Task<ELoginResult> LoginAsync(
            string username,
            string password,
            Func<Task<string?>> getGuardCode,
            Func<Task>? onAppConfirmPending = null,
            string? preSetGuardCode = null,
            Action<string>? onLogLine = null) {

            State = ELoginState.LoggingIn;
            LoggedInUsername = string.Empty;
            LogManager.Log.Information("Steam login started for user \"{Username}\"", username);

            try {
                var result = await RunLoginSessionAsync(username, password, getGuardCode, onAppConfirmPending, preSetGuardCode, onLogLine);

                if (result == ELoginResult.Success) {
                    State = ELoginState.LoggedIn;
                    LoggedInUsername = username;
                    LogManager.Log.Information("Steam login succeeded for user \"{Username}\"", username);
                } else {
                    State = ELoginState.Failed;
                    LogManager.Log.Warning("Steam login failed: {Result}", result);
                }
                return result;
            } catch (Exception ex) {
                LogManager.Log.Error("Exception occurred during Steam login:\n{Exception}", ex);
                State = ELoginState.Failed;
                return ELoginResult.Unknown;
            }
        }

        /// <summary>
        /// 启动 steamcmd，流式读取 stdout，在交互节点写入 stdin 或等待手机确认。
        /// </summary>
        private static async Task<ELoginResult> RunLoginSessionAsync(
            string username,
            string password,
            Func<Task<string?>> getGuardCode,
            Func<Task>? onAppConfirmPending,
            string? preSetGuardCode,
            Action<string>? onLogLine) {

            if (!File.Exists(Constants.SteamcmdFile)) {
                LogManager.Log.Error("Steamcmd not found at \"{Path}\"", Constants.SteamcmdFile);
                return ELoginResult.Unknown;
            }

            // 参数：+login username password [set_steam_guard_code code] +quit
            // 若预设了验证码，直接在命令行中传入
            string loginArgs = string.IsNullOrWhiteSpace(preSetGuardCode)
                ? $"+login {username} {password} +quit"
                : $"+login {username} {password} +set_steam_guard_code {preSetGuardCode.Trim().Replace(" ", "").ToUpperInvariant()} +quit";
            ProcessStartInfo startInfo = Helper.Steamcmd.GetProcessStartInfo(loginArgs);

            using Process process = Process.Start(startInfo)
                ?? throw new Exception("Failed to start steamcmd process");

            // 累积完整输出（用于最终解析）
            var outputBuilder = new StringBuilder();
            // 追踪是否已处理过 2FA 交互（防止重复触发）
            bool guardHandled = false;
            bool appConfirmHandled = false;
            ELoginResult? earlyResult = null;

            // 流式读取 stdout，按行处理交互
            var readTask = Task.Run(async () => {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync()) != null) {
                    outputBuilder.AppendLine(line);
                    LogManager.Log.Debug("steamcmd: {Line}", line);
                    onLogLine?.Invoke(line);

                    // --- App 确认（无需输入，手机操作）---
                    if (!appConfirmHandled &&
                        (line.Contains("Use the Steam Mobile App to confirm") ||
                         line.Contains("Steam Mobile App") ||
                         line.Contains("mobile app to confirm"))) {
                        appConfirmHandled = true;
                        guardHandled = true;
                        LogManager.Log.Information("Steam App confirmation required; waiting for mobile confirmation");
                        if (onAppConfirmPending != null) {
                            // 仅通知 UI 更新状态文字，不能阻塞此读取循环
                            // （否则 stdout 缓冲区会满导致 steamcmd 挂起）
                            _ = Task.Run(onAppConfirmPending);
                        }
                        // 不向 stdin 写入任何内容，steamcmd 自行等待手机确认
                    }
                    // --- TOTP / 邮箱验证码 ---
                    else if (!guardHandled && line.Contains("Two-factor code")) {
                        guardHandled = true;
                        LogManager.Log.Information("Steam Guard TOTP challenge received; requesting code from user");

                        string? code = await getGuardCode();
                        if (string.IsNullOrWhiteSpace(code)) {
                            LogManager.Log.Information("Steam login cancelled by user (no guard code entered)");
                            earlyResult = ELoginResult.Cancelled;
                            // 关闭 stdin 让 steamcmd 因 EOF 退出
                            try { process.StandardInput.Close(); } catch { }
                        } else {
                            var normalized = code.Trim().Replace(" ", "").ToUpperInvariant();
                            LogManager.Log.Information("Submitting Steam Guard TOTP code");
                            await process.StandardInput.WriteLineAsync(normalized);
                        }
                    }
                    // --- 邮箱验证码提示 ---
                    else if (!guardHandled &&
                             line.Contains("Steam Guard") && line.Contains("code")) {
                        guardHandled = true;
                        LogManager.Log.Information("Steam Guard email code challenge received; requesting code from user");

                        string? code = await getGuardCode();
                        if (string.IsNullOrWhiteSpace(code)) {
                            LogManager.Log.Information("Steam login cancelled by user (no guard code entered)");
                            earlyResult = ELoginResult.Cancelled;
                            try { process.StandardInput.Close(); } catch { }
                        } else {
                            var normalized = code.Trim().Replace(" ", "").ToUpperInvariant();
                            LogManager.Log.Information("Submitting Steam Guard email code");
                            await process.StandardInput.WriteLineAsync(normalized);
                        }
                    }
                }
            });

            // 同时消耗 stderr（防止缓冲区满导致死锁）
            var stderrTask = process.StandardError.ReadToEndAsync();

            // 超时时间：无 App 确认时 60s，检测到 App 确认后延长至 AppConfirmTimeoutSeconds
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var extendTimeoutIfNeeded = Task.Run(async () => {
                // 每秒检查一次，若已进入 App 确认阶段则延长超时
                while (!cts.IsCancellationRequested) {
                    await Task.Delay(1000);
                    if (appConfirmHandled) {
                        cts.CancelAfter(TimeSpan.FromSeconds(AppConfirmTimeoutSeconds));
                        break;
                    }
                }
            });

            try {
                await process.WaitForExitAsync(cts.Token);
            } catch (OperationCanceledException) {
                LogManager.Log.Warning("Steamcmd login timed out; killing process");
                try { process.Kill(); } catch { }
                try { await readTask; } catch { }
                await stderrTask;
                return ELoginResult.Unknown;
            }

            try { await readTask; } catch (Exception ex) {
                LogManager.Log.Warning("Exception reading steamcmd output stream: {Exception}", ex);
            }
            await stderrTask;

            if (earlyResult.HasValue)
                return earlyResult.Value;

            string output = outputBuilder.ToString();
            LogManager.Log.Debug("Steamcmd login full output:\n{Output}", output);
            return ParseLoginOutput(output);
        }

        /// <summary>
        /// 解析 Steamcmd 登录输出，返回对应结果。
        /// </summary>
        private static ELoginResult ParseLoginOutput(string output) {
            if (output.Contains("Logged in OK"))
                return ELoginResult.Success;

            if (output.Contains("Invalid login auth code") ||
                output.Contains("Two-factor code mismatch") ||
                output.Contains("incorrect Steam Guard code") ||
                output.Contains("Invalid Steam Guard"))
                return ELoginResult.GuardCodeFailed;

            if (output.Contains("Invalid Password") ||
                output.Contains("incorrect password"))
                return ELoginResult.InvalidPassword;

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
