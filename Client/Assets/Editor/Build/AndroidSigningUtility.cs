#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>固定街机 APK 签名；本机凭据与仓库中的公开证书指纹分离，不回退到机器调试密钥。</summary>
    public static class AndroidSigningUtility
    {
        /// <summary>默认使用本项目固定签名目录中的凭据（经用户授权明文入库）；节点可通过环境变量指定独立配置。</summary>
        public static string LocalSettingsPath => Environment.GetEnvironmentVariable("FIREPOWER_ANDROID_SIGNING_CONFIG") ??
            Path.Combine(WorkspaceRoot, ".codex", "signing", "android-signing.local.json");
        /// <summary>和资源服手动发布共用的公开证书白名单。</summary>
        public static string PolicyPath => Path.Combine(WorkspaceRoot, "Server", "ResourceServer", "android-signing.json");
        private static string WorkspaceRoot => Directory.GetParent(Application.dataPath).Parent.FullName;

        /// <summary>在创建构建目录前验证凭据、私钥可解锁及证书归属；不改 PlayerSettings。</summary>
        internal static void ValidateLocalKey(string settingsPath = null, string policyPath = null)
        {
            LoadValidatedSettings(settingsPath, policyPath);
        }

        /// <summary>只在 Player 构建调用期间使用固定签名，结束或异常时恢复调用者的全部签名设置。</summary>
        internal static IDisposable UseFixedSigning(string settingsPath = null, string policyPath = null)
        {
            var settings = LoadValidatedSettings(settingsPath, policyPath);
            return new SigningScope(settings);
        }

        /// <summary>校验 APK 的实际签名及证书，任何发布文件创建前执行；不需要私钥。</summary>
        public static void ValidateApk(string apkPath, string policyPath = null)
        {
            RunValidator("Apk", apkPath, policyPath ?? PolicyPath, null);
        }

        /// <summary>读取本机配置并验证真实私钥；日志不包含密码或配置原文。</summary>
        private static SigningSettings LoadValidatedSettings(string settingsPath, string policyPath)
        {
            string path = Path.GetFullPath(settingsPath ?? LocalSettingsPath);
            if (!File.Exists(path))
                throw new InvalidOperationException("缺少 FirepowerLoop 固定签名配置：" + path + "。请按 Tools/Android/README.md 配置原签名密钥，禁止回退到机器调试签名。");
            SigningSettings settings;
            try { settings = JsonUtility.FromJson<SigningSettings>(File.ReadAllText(path)); }
            catch { throw new InvalidOperationException("FirepowerLoop 本机签名配置无法解析。"); }
            if (settings == null || string.IsNullOrWhiteSpace(settings.keystorePath) ||
                string.IsNullOrWhiteSpace(settings.keyAlias) || string.IsNullOrEmpty(settings.keystorePassword) || string.IsNullOrEmpty(settings.keyPassword))
                throw new InvalidOperationException("FirepowerLoop 本机签名配置缺少 keystorePath、keyAlias 或密码。");
            settings.keystorePath = Path.GetFullPath(Path.IsPathRooted(settings.keystorePath)
                ? settings.keystorePath : Path.Combine(Path.GetDirectoryName(path), settings.keystorePath));
            RunValidator("Keystore", settings.keystorePath, policyPath ?? PolicyPath, settings);
            return settings;
        }

        /// <summary>通过共用脚本验证真实 Android 签名；并行排空输出管道，超时或失败均中止流程。</summary>
        private static void RunValidator(string mode, string input, string policy, SigningSettings settings)
        {
            string script = Path.Combine(WorkspaceRoot, "Server", "ResourceServer", "validate_android_signature.ps1");
            var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(script) +
                " -Mode " + mode + " -InputFile " + Quote(Path.GetFullPath(input)) + " -Policy " + Quote(Path.GetFullPath(policy)) +
                " -UnityExe " + Quote(EditorApplication.applicationPath) + (settings == null ? "" : " -KeyAlias " + Quote(settings.keyAlias)))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            if (settings != null)
            {
                info.EnvironmentVariables["FIREPOWER_SIGNING_STORE_PASSWORD"] = settings.keystorePassword;
                info.EnvironmentVariables["FIREPOWER_SIGNING_KEY_PASSWORD"] = settings.keyPassword;
            }
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("无法启动 Android 签名校验。");
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000))
                {
                    process.Kill();
                    throw new InvalidOperationException("Android 签名校验超时，已停止本次流程。");
                }
                string stdout = output.GetAwaiter().GetResult();
                string stderr = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Android 签名校验失败：" + stdout + "\n" + stderr);
            }
        }

        /// <summary>拒绝命令行无法无歧义表示的路径或别名；密码从不进入参数。</summary>
        private static string Quote(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("\"") || value.EndsWith("\\"))
                throw new ArgumentException("签名路径或别名不能含引号或以反斜线结尾。");
            return "\"" + value + "\"";
        }

        /// <summary>签名凭据配置，不参与序列化到项目 PlayerSettings 或发布包。</summary>
        [Serializable]
        private sealed class SigningSettings
        {
            public string keystorePath;
            public string keyAlias;
            public string keystorePassword;
            public string keyPassword;
        }

        /// <summary>签名配置的事务作用域，确保构建失败也不会把私钥路径或密码留给下次调用。</summary>
        private sealed class SigningScope : IDisposable
        {
            private readonly bool _custom = PlayerSettings.Android.useCustomKeystore;
            private readonly string _path = PlayerSettings.Android.keystoreName;
            private readonly string _alias = PlayerSettings.Android.keyaliasName;
            private readonly string _storePassword = PlayerSettings.Android.keystorePass;
            private readonly string _keyPassword = PlayerSettings.Android.keyaliasPass;

            public SigningScope(SigningSettings settings)
            {
                try
                {
                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = settings.keystorePath;
                    PlayerSettings.Android.keystorePass = settings.keystorePassword;
                    PlayerSettings.Android.keyaliasName = settings.keyAlias;
                    PlayerSettings.Android.keyaliasPass = settings.keyPassword;
                }
                catch { Dispose(); throw; }
            }

            public void Dispose()
            {
                PlayerSettings.Android.useCustomKeystore = _custom;
                PlayerSettings.Android.keystoreName = _path;
                PlayerSettings.Android.keystorePass = _storePassword;
                PlayerSettings.Android.keyaliasName = _alias;
                PlayerSettings.Android.keyaliasPass = _keyPassword;
            }
        }
    }
}
#endif
