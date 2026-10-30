#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace Game.Editor
{
    /// <summary>
    /// 读取既有 YooAsset 构建报告并按资源模块统计体积；只负责编辑器审计，不构建资源也不改变运行时下载策略。
    /// </summary>
    public static class ResourceModuleAuditUtility
    {
        public const string StartupTag = "startup";
        public const string ArcadeTag = "arcade";
        public const string ArcadeOnlineTag = "arcade-online";
        public const string DevelopmentTag = "development";

        private const string PackageName = "DefaultPackage";
        private const string SharedFontCollectorPath = "Assets/Game/Fonts/Chinese";


        /// <summary>允许写入收集器的稳定模块标签；未列出的标签会在构建前被拒绝。</summary>
        private static readonly HashSet<string> AllowedTags = new HashSet<string>(StringComparer.Ordinal)
        {
            StartupTag,
            ArcadeTag,
            ArcadeOnlineTag,
            DevelopmentTag
        };

        private static readonly HashSet<string> FirstDownloadTags = new HashSet<string>(StringComparer.Ordinal)
        {
            StartupTag
        };

        [MenuItem("Tools/打包/资源/构建 PC 模块测量包", false, 189)]
        private static void BuildWindowsMeasurementPackageMenu()
        {
            BuildMeasurementPackage(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        }

        /// <summary>
        /// 手动触发指定平台的纯资源构建；必要时切换平台，但不构建 Player 也不发布产物。
        /// </summary>
        private static void BuildMeasurementPackage(BuildTargetGroup targetGroup, BuildTarget target)
        {
            if (!EditorUtility.DisplayDialog(
                    "构建模块测量包",
                    $"将自动切换到 {target}，然后只构建 YooAsset 远端资源包。" +
                    "\n不构建 Player，也不发布到资源服务器。\n\n是否继续？",
                    "构建",
                    "取消"))
            {
                return;
            }

            if (!BuildPipeline.IsBuildTargetSupported(targetGroup, target))
            {
                string error = $"当前 Unity 未安装或不支持 {target} 构建模块。";
                Debug.LogError($"[ResourceModuleAudit] {error}");
                EditorUtility.DisplayDialog("模块测量包构建失败", error, "确定");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != target &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
            {
                string error = $"无法自动切换到 {target}。";
                Debug.LogError($"[ResourceModuleAudit] {error}");
                EditorUtility.DisplayDialog("模块测量包构建失败", error, "确定");
                return;
            }

            // 测量版本只用于区分本地报告，不参与运行时版本策略。
            string version = DateTime.Now.ToString("yyyyMMddHHmmss");
            YooAssetBuildUtility.BuildResult buildResult = YooAssetBuildUtility.BuildDefaultPackage(version, false);
            if (!buildResult.Success)
            {
                string error = buildResult.Error ?? "未知错误";
                Debug.LogError($"[ResourceModuleAudit] 模块测量包构建失败：{error}");
                EditorUtility.DisplayDialog("模块测量包构建失败", error, "确定");
                return;
            }

            string message = $"构建完成：{buildResult.OutputPackageDirectory}\n\n" +
                             "模块体积已输出到 Console，也可再执行“分析最新模块体积”。";
            Debug.Log($"[ResourceModuleAudit] {message}");
            EditorUtility.DisplayDialog("模块测量包构建完成", message, "确定");
        }

        [MenuItem("Tools/打包/资源/分析最新模块体积", false, 190)]
        private static void AnalyzeLatestReportMenu()
        {
            string packageRoot = YooAssetBuildUtility.GetPackageRoot(EditorUserBuildSettings.activeBuildTarget);
            if (!TryFindLatestReport(packageRoot, out string reportPath, out string findError))
            {
                Debug.LogError($"[ResourceModuleAudit] {findError}");
                EditorUtility.DisplayDialog("模块体积分析失败", findError, "确定");
                return;
            }

            if (!TryAnalyzeReport(reportPath, out ResourceModuleAuditResult result, out string auditError))
            {
                Debug.LogError($"[ResourceModuleAudit] {auditError}");
                EditorUtility.DisplayDialog("模块体积分析失败", auditError, "确定");
                return;
            }

            string summary = BuildSummary(result);
            if (result.UntaggedBundles.Count > 0 || result.UnknownTags.Count > 0)
                Debug.LogError(summary);
            else
                Debug.Log(summary);
            EditorUtility.DisplayDialog("模块体积分析", summary, "确定");
        }

        /// <summary>
        /// 在资源构建前校验每个启用收集器都具有合法模块标签，并确认中文字体由独立共享收集器持有。
        /// </summary>
        public static bool TryValidateCollectorTags(out string error)
        {
            error = null;
            AssetBundleCollectorPackage package;
            try
            {
                package = AssetBundleCollectorSettingData.Setting.GetPackage(PackageName);
            }
            catch (Exception exception)
            {
                error = $"读取 {PackageName} 收集配置失败：{exception.Message}";
                return false;
            }

            bool hasSharedFontCollector = false;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var startupPaths = new HashSet<string>(StringComparer.Ordinal);
            var scenePaths = new HashSet<string>(StringComparer.Ordinal);
            var directoryPackPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssetBundleCollectorGroup group in package.Groups)
            {
                if (group == null)
                    continue;

                var activeRule = AssetBundleCollectorSettingData.GetActiveRuleInstance(group.ActiveRuleName);
                if (!activeRule.IsActiveGroup(new GroupData(group.GroupName)))
                    continue;

                foreach (AssetBundleCollector collector in group.Collectors)
                {
                    if (collector == null)
                        continue;

                    string path = NormalizeAssetPath(collector.CollectPath);
                    if (!paths.Add(path))
                    {
                        error = $"资源收集路径重复：{path}";
                        return false;
                    }

                    if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                        scenePaths.Add(path.Substring(0, path.Length - ".unity".Length));
                    else if (string.Equals(collector.PackRuleName, "PackDirectory", StringComparison.Ordinal))
                        directoryPackPaths.Add(path);

                    string[] tags = ParseTags(group.AssetTags)
                        .Concat(ParseTags(collector.AssetTags))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (tags.Length == 0)
                    {
                        error = $"资源收集器未分类：{path}";
                        return false;
                    }

                    string unknownTag = tags.FirstOrDefault(tag => !AllowedTags.Contains(tag) && !tag.StartsWith("map-", StringComparison.Ordinal));
                    if (unknownTag != null)
                    {
                        error = $"资源收集器包含未知模块标签：{path} -> {unknownTag}";
                        return false;
                    }

                    if (tags.Contains(StartupTag, StringComparer.Ordinal)) startupPaths.Add(path);

                    if (string.Equals(path, SharedFontCollectorPath, StringComparison.Ordinal))
                    {
                        hasSharedFontCollector = tags.Contains(StartupTag, StringComparer.Ordinal) &&
                                                 string.Equals(collector.PackRuleName, "PackDirectory", StringComparison.Ordinal);
                    }
                }
            }

            // 场景文件与同名目录会得到相同的默认 Bundle 名，PackDirectory 会导致 SBP 混合 Scene 与普通 Asset。
            string mixedBundlePath = scenePaths.FirstOrDefault(directoryPackPaths.Contains);
            if (mixedBundlePath != null)
            {
                error = $"场景与同名目录不能共用 PackDirectory：{mixedBundlePath}.unity / {mixedBundlePath}";
                return false;
            }

            if (!hasSharedFontCollector)
            {
                error = $"缺少 startup 分类的共享字体收集器：{SharedFontCollectorPath}";
                return false;
            }


            return true;
        }

        /// <summary>
        /// 解析一次已经完成的 YooAsset 构建报告；未分类 Bundle 会作为审计结果返回，由调用方决定是否阻断。
        /// </summary>
        public static bool TryAnalyzeReport(string reportPath, out ResourceModuleAuditResult result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
            {
                error = $"YooAsset 构建报告不存在：{reportPath}";
                return false;
            }

            BuildReportDocument document;
            try
            {
                document = JsonUtility.FromJson<BuildReportDocument>(File.ReadAllText(reportPath));
            }
            catch (Exception exception)
            {
                error = $"YooAsset 构建报告解析失败：{exception.Message}";
                return false;
            }

            if (document?.Summary == null || document.BundleInfos == null)
            {
                error = $"YooAsset 构建报告缺少 Summary 或 BundleInfos：{reportPath}";
                return false;
            }

            var tagBytes = AllowedTags.ToDictionary(tag => tag, _ => 0L, StringComparer.Ordinal);
            var untagged = new List<string>();
            var unknown = new HashSet<string>(StringComparer.Ordinal);
            long totalBytes = 0;
            long firstDownloadBytes = 0;
            foreach (BuildReportBundle bundle in document.BundleInfos)
            {
                if (bundle == null)
                    continue;

                totalBytes += bundle.FileSize;
                string[] tags = bundle.Tags ?? Array.Empty<string>();
                if (tags.Length == 0)
                {
                    untagged.Add(bundle.BundleName ?? "<unnamed>");
                    continue;
                }

                bool firstDownload = false;
                foreach (string tag in tags.Distinct(StringComparer.Ordinal))
                {
                    if (tagBytes.ContainsKey(tag))
                        tagBytes[tag] += bundle.FileSize;
                    else if (tag.StartsWith("map-", StringComparison.Ordinal))
                        tagBytes[tag] = bundle.FileSize;
                    else
                        unknown.Add(tag);
                    if (FirstDownloadTags.Contains(tag))
                        firstDownload = true;
                }

                if (firstDownload)
                    firstDownloadBytes += bundle.FileSize;
            }

            result = new ResourceModuleAuditResult(
                Path.GetFullPath(reportPath),
                ((BuildTarget)document.Summary.BuildTarget).ToString(),
                document.Summary.BuildPackageVersion,
                document.Summary.BuildDate,
                document.BundleInfos.Length,
                totalBytes,
                firstDownloadBytes,
                tagBytes,
                untagged.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                unknown.OrderBy(value => value, StringComparer.Ordinal).ToArray());
            return true;
        }

        /// <summary>校验刚生成的报告具备完整模块分类，并返回适合构建日志记录的体积摘要。</summary>
        public static bool TryValidateBuiltReport(
            string packageDirectory,
            string version,
            out ResourceModuleAuditResult result,
            out string error)
        {
            string reportPath = Path.Combine(packageDirectory ?? string.Empty, $"{PackageName}_{version}.report");
            if (!TryAnalyzeReport(reportPath, out result, out error))
                return false;
            if (result.UntaggedBundles.Count > 0)
            {
                error = $"构建报告包含 {result.UntaggedBundles.Count} 个未分类 Bundle：" +
                        string.Join(", ", result.UntaggedBundles.Take(8));
                return false;
            }

            if (result.UnknownTags.Count > 0)
            {
                error = "构建报告包含未知模块标签：" + string.Join(", ", result.UnknownTags);
                return false;
            }

            return true;
        }

        public static string BuildSummary(ResourceModuleAuditResult result)
        {
            if (result == null)
                return "模块体积报告为空。";

            var lines = new List<string>
            {
                $"平台: {result.BuildTarget}",
                $"版本: {result.PackageVersion}",
                $"Bundle: {result.BundleCount} 个，共 {ToMiB(result.TotalBytes):F2} MiB",
                $"首次必需(startup：基地与河谷): {ToMiB(result.FirstDownloadBytes):F2} MiB"
            };
            foreach (string tag in AllowedTags.OrderBy(value => value, StringComparer.Ordinal))
                lines.Add($"{tag}: {ToMiB(result.TagBytes[tag]):F2} MiB");
            lines.Add($"未分类 Bundle: {result.UntaggedBundles.Count}");
            lines.Add($"未知标签: {result.UnknownTags.Count}");
            lines.Add($"报告: {result.ReportPath}");
            return string.Join("\n", lines);
        }

        private static bool TryFindLatestReport(string packageRoot, out string reportPath, out string error)
        {
            reportPath = null;
            error = null;
            if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
            {
                error = $"资源包输出目录不存在：{packageRoot}";
                return false;
            }

            reportPath = Directory.GetFiles(packageRoot, $"{PackageName}_*.report", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (reportPath != null)
                return true;
            error = $"未找到 YooAsset 构建报告：{packageRoot}";
            return false;
        }

        private static string[] ParseTags(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .ToArray();
        }

        private static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        private static double ToMiB(long bytes) => bytes / (1024d * 1024d);

        [Serializable]
        private sealed class BuildReportDocument
        {
            public BuildReportSummary Summary;
            public BuildReportBundle[] BundleInfos;
        }

        [Serializable]
        private sealed class BuildReportSummary
        {
            public int BuildTarget;
            public string BuildPackageVersion;
            public string BuildDate;
        }

        [Serializable]
        private sealed class BuildReportBundle
        {
            public string BundleName;
            public long FileSize;
            public string[] Tags;
        }
    }

    /// <summary>模块体积审计的不可变结果，单位统一使用字节，由展示层换算为 MiB。</summary>
    public sealed class ResourceModuleAuditResult
    {
        public ResourceModuleAuditResult(
            string reportPath,
            string buildTarget,
            string packageVersion,
            string buildDate,
            int bundleCount,
            long totalBytes,
            long firstDownloadBytes,
            IReadOnlyDictionary<string, long> tagBytes,
            IReadOnlyList<string> untaggedBundles,
            IReadOnlyList<string> unknownTags)
        {
            ReportPath = reportPath;
            BuildTarget = buildTarget;
            PackageVersion = packageVersion;
            BuildDate = buildDate;
            BundleCount = bundleCount;
            TotalBytes = totalBytes;
            FirstDownloadBytes = firstDownloadBytes;
            TagBytes = tagBytes;
            UntaggedBundles = untaggedBundles;
            UnknownTags = unknownTags;
        }

        public string ReportPath { get; }
        public string BuildTarget { get; }
        public string PackageVersion { get; }
        public string BuildDate { get; }
        public int BundleCount { get; }
        public long TotalBytes { get; }
        public long FirstDownloadBytes { get; }
        public IReadOnlyDictionary<string, long> TagBytes { get; }
        public IReadOnlyList<string> UntaggedBundles { get; }
        public IReadOnlyList<string> UnknownTags { get; }
    }
}
#endif
