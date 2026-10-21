#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>渠道候选包的只读检查与构建策略；检查通过仅代表技术条件，不代表真机或平台审核。</summary>
    public static class AndroidChannelBuildUtility
    {
        /// <summary>渠道私钥配置必须显式提供，不使用内部测试签名回退。</summary>
        public const string SigningConfigVariable = "FIREPOWER_ANDROID_CHANNEL_SIGNING_CONFIG";
        /// <summary>渠道公开证书策略独立于内部测试白名单，不包含私钥或密码。</summary>
        public const string SigningPolicyVariable = "FIREPOWER_ANDROID_CHANNEL_SIGNING_POLICY";

        /// <summary>内部入口保持 Development；渠道入口禁止调试及自动连接 Profiler。</summary>
        public static BuildOptions BuildOptionsFor(bool channel) => BuildOptions.CompressWithLz4 |
            (channel ? BuildOptions.None : BuildOptions.Development);

        /// <summary>依据已读取的 APK 事实生成逐项报告，供真实包检查和限定策略测试共同使用。</summary>
        public static string Evaluate(string factsJson, bool channel, string expectedCertificate) =>
            EvaluateProfile(factsJson, channel, false, expectedCertificate);

        /// <summary>离线候选包不要求公网地址，必须同时证明正式身份、离线标记和无联网权限。</summary>
        public static string EvaluateOffline(string factsJson, string expectedCertificate) =>
            EvaluateProfile(factsJson, true, true, expectedCertificate);

        private static string EvaluateProfile(string factsJson, bool channel, bool offline, string expectedCertificate)
        {
            JObject facts = JObject.Parse(factsJson);
            var checks = new JArray();
            void Check(string id, bool passed, string message) => checks.Add(new JObject
                { ["id"] = id, ["status"] = passed ? "Passed" : "Failed", ["message"] = message });
            string certificate = facts.Value<string>("certificateSha256") ?? "";
            Check("metadata", facts.Value<bool?>("metadataRead") != false, "Android SDK 元数据必须读取成功。");
            Check("package", facts.Value<string>("packageName") == AndroidBuildUtility.ApplicationIdentifier, "包名必须为本项目独立身份。");
            Check("version", Regex.IsMatch(facts.Value<string>("versionName") ?? "", @"^\d+\.\d+\.\d+$") &&
                facts.Value<int?>("versionCode") > 0, "实际 APK 必须包含有效版本名和正版本码。");
            Check("signature", facts.Value<bool?>("signatureValid") == true, "APK 签名完整性必须验证成功。");
            Check("certificate", Regex.IsMatch(expectedCertificate ?? "", @"\A[0-9a-fA-F]{64}\z") &&
                string.Equals(certificate, expectedCertificate, StringComparison.OrdinalIgnoreCase), "实际签名必须符合显式公开证书策略。");
            var architectures = facts["architectures"] as JArray;
            Check("architecture", architectures != null && architectures.Count == 1 && (string)architectures[0] == "arm64-v8a", "本项目 APK 必须仅包含 ARM64 架构。");
            if (channel)
            {
                Check("debuggable", facts.Value<bool?>("debuggable") == false, "渠道候选包不能启用 debuggable。");
                Check("testOnly", facts.Value<bool?>("testOnly") != true, "渠道候选包不能标记为 testOnly。");
                string subject = facts.Value<string>("certificateSubject") ?? "";
                Check("releaseCertificate", subject.Length > 0 && !Regex.IsMatch(subject,
                    @"(?:^|,)\s*CN\s*=\s*Android Debug\s*(?:,|$)", RegexOptions.IgnoreCase), "渠道候选包不能使用 Android 调试证书。");
                if (offline)
                {
                    var permissions = facts["permissions"] as JArray;
                    Check("offlineProfile", facts.Value<string>("releaseProfile") == "OfflineSinglePlayer", "实际 APK 必须包含纯离线首发标记。");
                    Check("offlinePermissions", permissions != null && !permissions.Values<string>().Contains("android.permission.INTERNET") &&
                        !permissions.Values<string>().Contains("android.permission.REQUEST_INSTALL_PACKAGES"), "离线包不得包含联网或自有安装更新权限。");
                }
                else
                {
                    Check("resourceServer", IsPublicHttps(facts.Value<string>("resourceServer")), "资源地址必须是无凭据、无片段的公网 HTTPS 地址。");
                    Check("gameGateEndpoint", IsPublicGate(facts.Value<string>("gameGateEndpoint")), "联机网关必须是公网主机及显式端口。");
                    Check("diagnostics", facts.Value<string>("diagnosticMode") == "Fixed" && IsPublicHttps(facts.Value<string>("diagnosticUrl")),
                        "渠道诊断必须显式指定公网 HTTPS 地址，LocalGameServer 与空地址不表示禁用。");
                }
            }
            if (!offline && (facts["permissions"] as JArray)?.Values<string>().Contains("android.permission.REQUEST_INSTALL_PACKAGES") == true)
                checks.Add(new JObject { ["id"] = "installerPermission", ["status"] = "Warning",
                    ["message"] = "包含自有 APK 安装权限；提交前需按目标渠道确定更新方案，此检查不代表平台接受该权限。" });
            return new JObject { ["schemaVersion"] = 1, ["profile"] = offline ? "OfflineSinglePlayerCandidate" : channel ? "ChannelCandidate" : "InternalTest",
                ["passed"] = checks.All(check => (string)check["status"] != "Failed"), ["deviceVerified"] = false,
                ["storeApproved"] = false, ["checkedUtc"] = DateTime.UtcNow.ToString("O"), ["facts"] = facts, ["checks"] = checks }.ToString(Formatting.Indented);
        }

        /// <summary>只调用 SDK 读取并验签已有 APK，不提取执行包内代码，不修改安装包。</summary>
        public static string Inspect(string apkPath, bool channel, string expectedCertificate) =>
            InspectProfile(apkPath, channel, false, expectedCertificate);

        /// <summary>读取真实已签名 APK；离线结论必须依据包内清单，不能只依赖编辑器配置。</summary>
        public static string InspectOffline(string apkPath, string expectedCertificate) =>
            InspectProfile(apkPath, true, true, expectedCertificate);

        private static string InspectProfile(string apkPath, bool channel, bool offline, string expectedCertificate)
        {
            apkPath = Path.GetFullPath(apkPath);
            if (!File.Exists(apkPath) || new FileInfo(apkPath).Length == 0) throw new FileNotFoundException("待检查 APK 不存在或为空。", apkPath);
            using (FileStream input = OpenInspectionInput(apkPath))
                return InspectLocked(input, apkPath, channel, offline, expectedCertificate);
        }

        /// <summary>整个检查期间只共享读取，阻止其他进程写入或替换，确保所有工具观察同一份 APK。</summary>
        private static FileStream OpenInspectionInput(string apkPath) =>
            new FileStream(apkPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        /// <summary>调用者持续持有 APK 读取锁；SDK、哈希及 ZIP 配置使用同一份文件内容。</summary>
        private static string InspectLocked(FileStream input, string apkPath, bool channel, bool offline, string expectedCertificate)
        {
            string tools = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data/PlaybackEngines/AndroidPlayer");
            string buildTools = Directory.GetDirectories(Path.Combine(tools, "SDK/build-tools"))
                .Where(path => Version.TryParse(Path.GetFileName(path), out _))
                .OrderByDescending(path => Version.Parse(Path.GetFileName(path)))
                .FirstOrDefault(path => File.Exists(Path.Combine(path, "aapt.exe")) && File.Exists(Path.Combine(path, "lib/apksigner.jar")));
            if (buildTools == null) throw new InvalidOperationException("缺少同时包含 aapt 与 apksigner 的 Android build-tools。");
            string badging = RunTool(Path.Combine(buildTools, "aapt.exe"), new[] { "dump", "badging", apkPath }, out int badgingCode);
            string manifest = RunTool(Path.Combine(buildTools, "aapt.exe"), new[] { "dump", "xmltree", apkPath, "AndroidManifest.xml" }, out int manifestCode);
            string signature = RunTool(Path.Combine(tools, "OpenJDK/bin/java.exe"), new[] { "-jar", Path.Combine(buildTools, "lib/apksigner.jar"), "verify", "--print-certs", apkPath }, out int signatureCode);
            Match package = Regex.Match(badging, @"(?m)^package: name='([^']*)' versionCode='([^']*)' versionName='([^']*)'");
            var fingerprints = Regex.Matches(signature, @"(?m)^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]{64})\s*$");
            var facts = new JObject
            {
                ["apkPath"] = apkPath, ["bytes"] = new FileInfo(apkPath).Length,
                ["metadataRead"] = badgingCode == 0 && manifestCode == 0,
                ["packageName"] = package.Groups[1].Value, ["versionName"] = package.Groups[3].Value,
                ["versionCode"] = int.TryParse(package.Groups[2].Value, out int code) ? code : 0,
                ["minSdk"] = Regex.Match(badging, @"(?m)^sdkVersion:'([^']*)'").Groups[1].Value,
                ["targetSdk"] = Regex.Match(badging, @"(?m)^targetSdkVersion:'([^']*)'").Groups[1].Value,
                ["debuggable"] = badging.Contains("application-debuggable") || TrueManifestFlag(manifest, "debuggable"),
                ["testOnly"] = TrueManifestFlag(manifest, "testOnly"), ["signatureValid"] = signatureCode == 0 && fingerprints.Count == 1,
                ["toolExitCodes"] = new JObject { ["badging"] = badgingCode, ["manifest"] = manifestCode, ["signature"] = signatureCode },
                ["certificateSha256"] = fingerprints.Count == 1 ? fingerprints[0].Groups[1].Value.ToLowerInvariant() : "",
                ["certificateSubject"] = Regex.Match(signature, @"(?m)^Signer #1 certificate DN: (.+)$").Groups[1].Value.Trim(),
                ["architectures"] = new JArray(Regex.Matches(Regex.Match(badging, @"(?m)^native-code: (.+)$").Groups[1].Value, "'([^']+)'").Cast<Match>().Select(match => match.Groups[1].Value)),
                ["releaseProfile"] = AndroidOfflineReleaseBuildUtility.ReadManifestProfile(manifest),
                ["permissions"] = new JArray(Regex.Matches(badging, @"(?m)^uses-permission(?:-sdk-\d+)?: name='([^']+)'").Cast<Match>().Select(match => match.Groups[1].Value))
            };
            using (var hash = SHA256.Create()) facts["sha256"] = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            input.Position = 0;
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read, true))
            {
                facts["resourceServer"] = ReadConfig(archive, "assets/hostConfig.json").Value<string>("resourceServer") ?? "";
                facts["gameGateEndpoint"] = ReadConfig(archive, "assets/networkConfig.json").Value<string>("gameGateEndpoint") ?? "";
                var diagnostics = ReadConfig(archive, "assets/diagnosticConfig.json");
                facts["diagnosticMode"] = diagnostics.Value<string>("mode") ?? "";
                facts["diagnosticUrl"] = diagnostics.Value<string>("reportUrl") ?? "";
            }
            return offline ? EvaluateOffline(facts.ToString(), expectedCertificate) : Evaluate(facts.ToString(), channel, expectedCertificate);
        }

        /// <summary>构建前验证外部正式签名与包内服务配置；不切平台、不创建产物或分配版本。</summary>
        internal static void ValidateChannelConfiguration()
        {
            ValidateOfflineChannelConfiguration();
            var facts = new JObject
            {
                ["resourceServer"] = JObject.Parse(File.ReadAllText("Assets/StreamingAssets/hostConfig.json")).Value<string>("resourceServer"),
                ["gameGateEndpoint"] = JObject.Parse(File.ReadAllText("Assets/StreamingAssets/networkConfig.json")).Value<string>("gameGateEndpoint")
            };
            var diagnostic = JObject.Parse(File.ReadAllText("Assets/StreamingAssets/diagnosticConfig.json"));
            if (!IsPublicHttps((string)facts["resourceServer"]) || !IsPublicGate((string)facts["gameGateEndpoint"]) ||
                diagnostic.Value<string>("mode") != "Fixed" || !IsPublicHttps(diagnostic.Value<string>("reportUrl")))
                throw new InvalidOperationException("渠道资源、联机及诊断配置仍含内网、占位或不安全地址；请先完成正式环境配置。");
        }

        /// <summary>离线候选只验证独立正式签名，不要求并不会被使用的公网服务配置。</summary>
        internal static void ValidateOfflineChannelConfiguration()
        {
            string settings = RequireEnvironmentFile(SigningConfigVariable);
            string policy = RequireEnvironmentFile(SigningPolicyVariable);
            string fingerprint = ReadFingerprint(policy);
            if (fingerprint == ReadFingerprint(AndroidSigningUtility.PolicyPath)) throw new InvalidOperationException("渠道证书不能复用项目内部测试证书。");
            AndroidSigningUtility.ValidateLocalKey(settings, policy);
        }

        /// <summary>仅在渠道 Player 构建的作用域内应用显式正式签名，结束后由既有签名事务恢复设置。</summary>
        internal static IDisposable UseChannelSigning() => AndroidSigningUtility.UseFixedSigning(
            RequireEnvironmentFile(SigningConfigVariable), RequireEnvironmentFile(SigningPolicyVariable));

        /// <summary>渠道产物必须使用当前显式证书策略验签，环境变量缺失时不回退到测试策略。</summary>
        internal static void ValidateChannelSignature(string apkPath) => AndroidSigningUtility.ValidateApk(
            apkPath, RequireEnvironmentFile(SigningPolicyVariable));

        /// <summary>已有包检查和构建后门禁共用报告；失败也保存原因，报告路径不得覆盖 APK 或已有文件。</summary>
        public static string InspectAndSave(string apkPath, string outputPath, bool channel) =>
            SaveInspection(apkPath, outputPath, channel, false);

        /// <summary>保存离线候选实际包检查报告；使用独立正式证书策略。</summary>
        public static string InspectOfflineAndSave(string apkPath, string outputPath) =>
            SaveInspection(apkPath, outputPath, true, true);

        private static string SaveInspection(string apkPath, string outputPath, bool channel, bool offline)
        {
            string policy = channel ? Environment.GetEnvironmentVariable(SigningPolicyVariable) : AndroidSigningUtility.PolicyPath;
            string fingerprint = string.IsNullOrWhiteSpace(policy) ? "" : ReadFingerprint(policy);
            string json = offline ? InspectOffline(apkPath, fingerprint) : Inspect(apkPath, channel, fingerprint);
            string output = Path.GetFullPath(outputPath);
            if (File.Exists(output) || string.Equals(output, Path.GetFullPath(apkPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("检查报告路径已存在或与 APK 相同，拒绝覆盖。");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(json);
            return json;
        }

        [MenuItem("Tools/打包/Android/检查现有APK（内部测试，不打包）", false, 156)]
        private static void InspectInternalMenu() => InspectMenu(false);
        [MenuItem("Tools/打包/Android/检查现有APK（渠道候选，不打包）", false, 157)]
        private static void InspectChannelMenu() => InspectMenu(true);

        [MenuItem("Tools/打包/Android/检查现有APK（纯离线单机，不打包）", false, 159)]
        private static void InspectOfflineMenu() => InspectMenu(true, true);

        private static void InspectMenu(bool channel, bool offline = false)
        {
            string apk = EditorUtility.OpenFilePanel("选择已有 APK", "", "apk");
            if (string.IsNullOrEmpty(apk)) return;
            string output = EditorUtility.SaveFilePanel("保存只读检查报告", Path.GetDirectoryName(apk), Path.GetFileNameWithoutExtension(apk) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-check", "json");
            if (string.IsNullOrEmpty(output)) return;
            try
            {
                var result = JObject.Parse(offline ? InspectOfflineAndSave(apk, output) : InspectAndSave(apk, output, channel));
                UnityEngine.Debug.Log("[AndroidChannelBuildUtility] APK 技术检查 " + ((bool)result["passed"] ? "通过" : "未通过") + "，报告：" + output + "；未执行打包或发布。");
            }
            catch (Exception exception) { UnityEngine.Debug.LogError("[AndroidChannelBuildUtility] APK 检查失败：" + exception.Message); }
        }

        private static JObject ReadConfig(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name);
            if (entry == null) return new JObject();
            if (entry.Length > 65536) throw new InvalidOperationException("包内启动配置超过检查上限：" + name);
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, true)) return JObject.Parse(reader.ReadToEnd());
        }

        private static bool TrueManifestFlag(string xmlTree, string name) => Regex.IsMatch(xmlTree,
            @"android:" + Regex.Escape(name) + @"\([^)]*\)=\(type 0x12\)0x(?:ffffffff|1)\b");

        private static string ReadFingerprint(string policy)
        {
            var value = JObject.Parse(File.ReadAllText(Path.GetFullPath(policy)));
            string fingerprint = value.Value<string>("certificateSha256");
            if (value.Value<int?>("schemaVersion") != 1 || !Regex.IsMatch(fingerprint ?? "", @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidOperationException("公开证书策略格式无效。");
            return fingerprint.ToLowerInvariant();
        }

        private static string RequireEnvironmentFile(string variable)
        {
            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
                throw new InvalidOperationException("请显式设置有效文件的绝对路径：" + variable + "；不会回退到内部测试签名。");
            return path;
        }

        private static bool IsPublicHttps(string value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
            Uri.TryCreate(value, UriKind.Absolute, out Uri uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && PublicHost(uri.Host);

        private static bool IsPublicGate(string value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
            Uri.TryCreate("tcp://" + value, UriKind.Absolute, out Uri uri) && uri.Port > 0 && uri.Port <= 65535 &&
            uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && PublicHost(uri.Host);

        private static bool PublicHost(string host)
        {
            host = host.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
            if (IPAddress.TryParse(host, out IPAddress ip))
            {
                if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
                if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) ||
                    ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
                byte[] bytes = ip.GetAddressBytes();
                if (ip.AddressFamily == AddressFamily.InterNetworkV6) return (bytes[0] & 0xe0) == 0x20 &&
                    !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
                return bytes[0] != 0 && bytes[0] != 10 && bytes[0] != 127 && bytes[0] < 224 &&
                    !(bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) && !(bytes[0] == 192 && bytes[1] == 168) &&
                    !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) &&
                    !(bytes[0] == 192 && bytes[1] == 0 && (bytes[2] == 0 || bytes[2] == 2)) &&
                    !(bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19 || (bytes[1] == 51 && bytes[2] == 100))) &&
                    !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
            }
            return host.Contains(".") && Uri.CheckHostName(host) == UriHostNameType.Dns &&
                !new[] { "localhost", "local", "lan", "internal", "test", "example", "invalid", "example.com", "example.net", "example.org" }
                    .Any(suffix => host == suffix || host.EndsWith("." + suffix, StringComparison.Ordinal));
        }

        /// <summary>外部工具无 Shell 执行；并行排空管道，超时只结束本次创建的子进程。</summary>
        private static string RunTool(string executable, IEnumerable<string> arguments, out int exitCode)
        {
            string Quote(string value)
            {
                if (value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 || value.EndsWith("\\", StringComparison.Ordinal))
                    throw new ArgumentException("SDK 参数含不支持的引号、换行或末尾反斜线。");
                return "\"" + value + "\"";
            }
            var info = new ProcessStartInfo(executable, string.Join(" ", arguments.Select(Quote)))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("无法启动 Android SDK 检查工具。");
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new TimeoutException("Android SDK 只读检查超时。"); }
                exitCode = process.ExitCode;
                return stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult();
            }
        }
    }
}
#endif
