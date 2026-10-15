#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace Game.Editor
{
    /// <summary>
    /// 支撑编辑器构建流程中的 YooAssetBuildUtility，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    public static class YooAssetBuildUtility
    {
        private const string PACKAGE_NAME = "DefaultPackage";
        private const string BUILD_PIPELINE = "ScriptableBuildPipeline";
        private const string ManifestOnlyBuiltinTag = "__AnotherDayManifestOnlyBuiltin__";
        private const string RequiredAddressRule = "AddressByGameRelativePathNoExtension";
        private const string RequiredFilterRule = "CollectAll";
        private static readonly string[] FixedRuntimePrefabLocations =
        {
            "Prefabs/Characters/GrayboxEnemy",
            "Prefabs/Characters/GrayboxPlayer",
            "Prefabs/Combat/GrayboxArrow",
            "Prefabs/SceneObjects/TestResourcePickup",
            "Prefabs/UI/Common/Loading",
            "Prefabs/UI/Diagnostics/RuntimeLog",
            "Prefabs/UI/Diagnostics/TestCmd",
            "Prefabs/UI/Arcade/ArcadeHud",
            "Prefabs/UI/Arcade/ArcadeShop",
            "Prefabs/UI/Arcade/ArcadeResume",
            "Prefabs/UI/Online/OnlineLogin",
            "Prefabs/UI/Online/OnlineRoom",
            "Prefabs/UI/Home/Setting",
            "Prefabs/UI/Home/Start",
            "Prefabs/UI/Notify/Float",
            "Prefabs/UI/Notify/Message"
        };

        public static IReadOnlyList<string> RuntimePrefabLocations
        {
            get
            {
                if (!TryBuildRequiredRuntimePrefabLocations(out var locations, out var error))
                {
                    throw new InvalidOperationException(error);
                }

                return locations;
            }
        }

    /// <summary>
    /// 支撑编辑器构建流程中的 BuildResult，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
        public class BuildResult
        {
            public bool Success;
            public string OutputPackageDirectory;
            public string PackageVersion;
            public string Error;
        }

        /// <summary>
        /// 构建 DefaultPackage；完整打包会清理并复制全部内置资源，Host 流程只产出远端包。
        /// </summary>
        public static BuildResult BuildDefaultPackage(string version, bool fullBuiltin)
        {
            return BuildPackage(
                version,
                fullBuiltin ? EBuildinFileCopyOption.ClearAndCopyAll : EBuildinFileCopyOption.None,
                string.Empty,
                fullBuiltin);
        }

        /// <summary>
        /// 构建首包对应的完整远端包，同时清空 StreamingAssets 并只写入资源清单和空内置目录。
        /// </summary>
        public static BuildResult BuildManifestOnlyBuiltinPackage(string version)
        {
            return BuildPackage(
                version,
                EBuildinFileCopyOption.ClearAndCopyByTags,
                ManifestOnlyBuiltinTag,
                false);
        }

        private static BuildResult BuildPackage(
            string version,
            EBuildinFileCopyOption buildinFileCopyOption,
            string buildinFileCopyParams,
            bool validateBuiltinManifest)
        {
            Debug.Log(
                $"[YooAssetBuildUtility] 开始构建资源包：{PACKAGE_NAME}，version={version}，" +
                $"内置复制方式={buildinFileCopyOption}");
            var result = new BuildResult { PackageVersion = version };

            try
            {
                if (!ResourceModuleAuditUtility.TryValidateCollectorTags(out var moduleTagError))
                {
                    result.Success = false;
                    result.Error = moduleTagError;
                    Debug.LogError($"[YooAssetBuildUtility] Resource module tag validation failed: {moduleTagError}");
                    return result;
                }

                if (!ValidateRequiredRuntimePrefabSources(out var sourceError))
                {
                    result.Success = false;
                    result.Error = sourceError;
                    Debug.LogError($"[YooAssetBuildUtility] Runtime prefab source validation failed: {sourceError}");
                    return result;
                }

                var parameters = new ScriptableBuildParameters
                {
                    PackageName = PACKAGE_NAME,
                    PackageVersion = version,
                    BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                    BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                    BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                    BuildPipeline = BUILD_PIPELINE,
                    BuildBundleType = (int)YooAsset.EBuildBundleType.AssetBundle,
                    BuildinFileCopyOption = buildinFileCopyOption,
                    BuildinFileCopyParams = buildinFileCopyParams,
                    FileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(PACKAGE_NAME, BUILD_PIPELINE),
                    CompressOption = AssetBundleBuilderSetting.GetPackageCompressOption(PACKAGE_NAME, BUILD_PIPELINE),
                    VerifyBuildingResult = true,
                    ClearBuildCacheFiles = false,
                    EncryptionServices = null,
                    ManifestProcessServices = null,
                    ManifestRestoreServices = null,
                    BuiltinShadersBundleName = "unity_builtin_shaders"
                };

                var pipeline = new ScriptableBuildPipeline();
                var buildResult = pipeline.Run(parameters, true);
                if (!buildResult.Success)
                {
                    result.Success = false;
                    result.Error = $"资源包构建任务失败：{buildResult.FailedTask}，底层错误：{buildResult.ErrorInfo}";
                    Debug.LogError($"[YooAssetBuildUtility] 资源包构建失败：{result.Error}");
                    return result;
                }

                result.Success = true;
                result.OutputPackageDirectory = buildResult.OutputPackageDirectory;
                if (!ValidateRequiredRuntimePrefabManifest(result.OutputPackageDirectory, version, out var manifestError))
                {
                    result.Success = false;
                    result.Error = manifestError;
                    Debug.LogError($"[YooAssetBuildUtility] Output manifest runtime prefab validation failed: {manifestError}");
                    return result;
                }

                // 构建报告是模块体积与标签闭包的最终权威；存在未分类 Bundle 时拒绝发布半可用结果。
                if (!ResourceModuleAuditUtility.TryValidateBuiltReport(
                        result.OutputPackageDirectory,
                        version,
                        out var moduleAudit,
                        out var moduleAuditError))
                {
                    result.Success = false;
                    result.Error = moduleAuditError;
                    Debug.LogError($"[YooAssetBuildUtility] Resource module report validation failed: {moduleAuditError}");
                    return result;
                }

                Debug.Log($"[YooAssetBuildUtility] 模块体积审计通过：\n{ResourceModuleAuditUtility.BuildSummary(moduleAudit)}");

                if (validateBuiltinManifest &&
                    !ValidateRequiredRuntimePrefabManifest(GetStreamingAssetsDirectory(), version, out var builtinManifestError))
                {
                    result.Success = false;
                    result.Error = builtinManifestError;
                    Debug.LogError($"[YooAssetBuildUtility] StreamingAssets manifest runtime prefab validation failed: {builtinManifestError}");
                    return result;
                }
                Debug.Log($"[YooAssetBuildUtility] 资源包输出目录：{result.OutputPackageDirectory}");
                return result;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Error = $"资源包构建异常：{e.Message}";
                Debug.LogError($"[YooAssetBuildUtility] 资源包构建异常：{e.Message}");
                Debug.LogException(e);
                return result;
            }
        }

        /// <summary>
        /// 校验首包只包含清单和空内置目录，禁止旧 Bundle 残留导致安装包体积回涨。
        /// </summary>
        public static bool ValidateManifestOnlyBuiltinPackage(string version)
        {
            string directory = GetStreamingAssetsDirectory();
            if (!Directory.Exists(directory))
                return Fail($"首包内置目录不存在: {directory}");

            string versionFile = Path.Combine(directory, $"{PACKAGE_NAME}.version");
            if (!File.Exists(versionFile))
                return Fail($"首包版本文件不存在: {versionFile}");
            string actualVersion = File.ReadAllText(versionFile).Trim();
            if (!string.Equals(actualVersion, version, StringComparison.Ordinal))
                return Fail($"首包版本不匹配，期望 {version}，实际 {actualVersion}");

            string manifestFile = Path.Combine(directory, $"{PACKAGE_NAME}_{version}.bytes");
            string hashFile = Path.Combine(directory, $"{PACKAGE_NAME}_{version}.hash");
            string catalogBinary = Path.Combine(directory, "BuildinCatalog.bytes");
            string catalogJson = Path.Combine(directory, "BuildinCatalog.json");
            foreach (string requiredFile in new[] { manifestFile, hashFile, catalogBinary, catalogJson })
            {
                if (!File.Exists(requiredFile) || new FileInfo(requiredFile).Length == 0)
                    return Fail($"首包必要清单不存在或为空: {requiredFile}");
            }

            foreach (string file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) ||
                    (name.Length == 32 && !name.Contains(".")))
                {
                    return Fail($"首包不应包含 Bundle: {file}");
                }
            }

            var catalog = JsonUtility.FromJson<ManifestOnlyCatalog>(File.ReadAllText(catalogJson));
            if (catalog == null || catalog.Wrappers == null)
                return Fail($"首包内置目录无法解析: {catalogJson}");
            if (catalog.Wrappers.Count != 0)
                return Fail($"首包内置目录应为空，实际记录 {catalog.Wrappers.Count} 个 Bundle");

            Debug.Log($"[YooAssetBuildUtility] 首包清单校验通过：{directory}，version={version}");
            return true;
        }

        [Serializable]
        private sealed class ManifestOnlyCatalog
        {
            public List<ManifestOnlyCatalogEntry> Wrappers;
        }

        [Serializable]
        private sealed class ManifestOnlyCatalogEntry
        {
            public string BundleGUID;
            public string FileName;
        }

        public static string GetStreamingAssetsDirectory()
        {
            return Path.Combine(AssetBundleBuilderHelper.GetStreamingAssetsRoot(), PACKAGE_NAME);
        }

        public static bool ValidateRequiredRuntimePrefabSources(out string error)
        {
            error = null;
            // 资源收集器校验前先阻断粉色材质风险，确保所有运行时 Prefab 都满足当前 URP 合同。
            if (!PrefabMaterialValidator.TryValidateGamePrefabs(out error))
            {
                return false;
            }

            if (!TryBuildRequiredRuntimePrefabLocations(out var requiredLocations, out error))
            {
                return false;
            }

            AssetBundleCollectorPackage package;
            try
            {
                package = AssetBundleCollectorSettingData.Setting.GetPackage(PACKAGE_NAME);
            }
            catch (Exception e)
            {
                error = $"DefaultPackage collector setting missing: {e.Message}";
                return false;
            }

            var activeCollectors = new List<AssetBundleCollector>();
            foreach (var group in package.Groups)
            {
                if (group == null)
                    continue;

                var activeRule = AssetBundleCollectorSettingData.GetActiveRuleInstance(group.ActiveRuleName);
                if (!activeRule.IsActiveGroup(new GroupData(group.GroupName)))
                    continue;

                activeCollectors.AddRange(group.Collectors.Where(collector => collector != null));
            }

            foreach (var location in requiredLocations)
            {
                var assetPath = ToRequiredPrefabAssetPath(location);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) == null)
                {
                    error = $"{location}: missing GameObject prefab asset at {assetPath}";
                    return false;
                }

                var collector = FindCoveringCollector(activeCollectors, assetPath);
                if (collector == null)
                {
                    error = $"{location}: no enabled collector covers {assetPath}";
                    return false;
                }

                if (collector.CollectorType != ECollectorType.MainAssetCollector)
                {
                    error = $"{location}: collector {collector.CollectPath} type is {collector.CollectorType}, expected MainAssetCollector";
                    return false;
                }

                if (!string.Equals(collector.AddressRuleName, RequiredAddressRule, StringComparison.Ordinal))
                {
                    error = $"{location}: collector {collector.CollectPath} address rule is {collector.AddressRuleName}, expected {RequiredAddressRule}";
                    return false;
                }

                if (!string.Equals(collector.FilterRuleName, RequiredFilterRule, StringComparison.Ordinal))
                {
                    error = $"{location}: collector {collector.CollectPath} filter rule is {collector.FilterRuleName}, expected {RequiredFilterRule}";
                    return false;
                }
            }

            Debug.Log("[YooAssetBuildUtility] Runtime prefab source validation passed.");
            return true;
        }

        public static bool ValidateRequiredRuntimePrefabManifest(string packageDirectory, string version, out string error)
        {
            error = null;
            if (!TryBuildRequiredRuntimePrefabLocations(out var requiredLocations, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
            {
                error = $"Manifest directory missing: {packageDirectory}";
                return false;
            }

            var versionFile = Path.Combine(packageDirectory, $"{PACKAGE_NAME}.version");
            if (!File.Exists(versionFile))
            {
                error = $"Version file missing: {versionFile}";
                return false;
            }

            var fileVersion = File.ReadAllText(versionFile).Trim();
            if (!string.Equals(fileVersion, version, StringComparison.Ordinal))
            {
                error = $"Version mismatch in {versionFile}: expected {version}, actual {fileVersion}";
                return false;
            }

            var manifestFile = Path.Combine(packageDirectory, $"{PACKAGE_NAME}_{version}.bytes");
            if (!File.Exists(manifestFile) || new FileInfo(manifestFile).Length == 0)
            {
                error = $"Manifest file missing or empty: {manifestFile}";
                return false;
            }

            object manifest;
            MethodInfo mappingMethod;
            try
            {
                manifest = DeserializePackageManifest(File.ReadAllBytes(manifestFile));
                if (manifest == null)
                {
                    error = $"Manifest deserialize returned null: {manifestFile}";
                    return false;
                }

                mappingMethod = manifest.GetType().GetMethod(
                    "TryMappingToAssetPath",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);
                if (mappingMethod == null)
                {
                    error = $"Manifest TryMappingToAssetPath method missing: {manifest.GetType().FullName}";
                    return false;
                }
            }
            catch (Exception e)
            {
                error = $"Manifest deserialize failed: {manifestFile}; {e.Message}";
                return false;
            }

            foreach (var location in requiredLocations)
            {
                var expected = ToRequiredPrefabAssetPath(location);
                string actual;
                try
                {
                    actual = mappingMethod.Invoke(manifest, new object[] { location }) as string;
                }
                catch (Exception e)
                {
                    error = $"{location}: TryMappingToAssetPath failed in {manifestFile}; {e.InnerException?.Message ?? e.Message}";
                    return false;
                }

                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    error = $"{location}: manifest maps to '{actual ?? string.Empty}', expected '{expected}' in {manifestFile}";
                    return false;
                }
            }

            Debug.Log($"[YooAssetBuildUtility] Runtime prefab manifest validation passed: {packageDirectory}, version={version}");
            return true;
        }

        private static AssetBundleCollector FindCoveringCollector(IReadOnlyList<AssetBundleCollector> collectors, string assetPath)
        {
            AssetBundleCollector best = null;
            var bestLength = -1;
            foreach (var collector in collectors)
            {
                if (collector == null || string.IsNullOrWhiteSpace(collector.CollectPath))
                    continue;

                var collectPath = NormalizeAssetPath(collector.CollectPath);
                var normalizedAssetPath = NormalizeAssetPath(assetPath);
                var covers = string.Equals(collectPath, normalizedAssetPath, StringComparison.Ordinal) ||
                             (AssetDatabase.IsValidFolder(collectPath) &&
                              normalizedAssetPath.StartsWith(collectPath.TrimEnd('/') + "/", StringComparison.Ordinal));
                if (covers && collectPath.Length > bestLength)
                {
                    best = collector;
                    bestLength = collectPath.Length;
                }
            }

            return best;
        }

        private static bool TryBuildRequiredRuntimePrefabLocations(out IReadOnlyList<string> locations, out string error)
        {
            locations = null;
            error = null;
            try
            {
                locations = FixedRuntimePrefabLocations
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                return true;
            }
            catch (Exception exception)
            {
                error = $"failed to enumerate arcade runtime prefabs: {exception.Message}";
                return false;
            }
        }

        private static string ToRequiredPrefabAssetPath(string location)
        {
            return $"Assets/Game/{location}.prefab";
        }

        private static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        private static object DeserializePackageManifest(byte[] bytes)
        {
            var manifestToolsType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YooAsset.ManifestTools", false))
                .FirstOrDefault(type => type != null);
            if (manifestToolsType == null)
                throw new InvalidOperationException("YooAsset.ManifestTools type not found.");

            var method = manifestToolsType.GetMethod(
                "DeserializeFromBinary",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException("YooAsset.ManifestTools.DeserializeFromBinary not found.");

            return method.Invoke(null, new object[] { bytes, null });
        }

        public static string GetPackageRoot(BuildTarget target)
        {
            return Path.GetFullPath(Path.Combine(
                AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                target.ToString(),
                PACKAGE_NAME));
        }

        internal static bool TryFindLatestValidPackage(string packageRoot, out string packageDirectory, out string version, out string error)
        {
            packageDirectory = null;
            version = null;

            string normalizedRoot = Path.GetFullPath(packageRoot);
            if (!Directory.Exists(normalizedRoot))
            {
                error = $"资源包根目录不存在，未找到有效资源版本: {normalizedRoot}";
                return false;
            }

            string[] candidates = Directory.GetDirectories(normalizedRoot);
            Array.Sort(candidates, (left, right) => string.Compare(
                Path.GetFileName(right),
                Path.GetFileName(left),
                StringComparison.Ordinal));

            foreach (string candidate in candidates)
            {
                string candidateVersion = Path.GetFileName(candidate);
                string versionFile = Path.Combine(candidate, $"{PACKAGE_NAME}.version");
                string fileVersion;
                try
                {
                    if (!File.Exists(versionFile))
                        continue;

                    fileVersion = File.ReadAllText(versionFile).Trim();
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(fileVersion))
                    continue;
                if (!string.Equals(candidateVersion, fileVersion, StringComparison.Ordinal))
                    continue;
                if (!IsValidPackageCandidate(candidate, fileVersion))
                    continue;

                packageDirectory = Path.GetFullPath(candidate);
                version = fileVersion;
                error = null;
                return true;
            }

            error = $"没有找到有效资源版本: {normalizedRoot}";
            return false;
        }

        private static bool IsValidPackageCandidate(string outputPackageDirectory, string version)
        {
            if (!Directory.Exists(outputPackageDirectory))
                return false;

            string versionFile = Path.Combine(outputPackageDirectory, $"{PACKAGE_NAME}.version");
            if (!File.Exists(versionFile))
                return false;

            string fileVersion;
            try
            {
                fileVersion = File.ReadAllText(versionFile).Trim();
            }
            catch
            {
                return false;
            }

            if (!string.Equals(fileVersion, version, StringComparison.Ordinal))
                return false;

            string manifestFile = Path.Combine(outputPackageDirectory, $"{PACKAGE_NAME}_{version}.bytes");
            if (!File.Exists(manifestFile) || new FileInfo(manifestFile).Length == 0)
                return false;

            string hashFile = Path.Combine(outputPackageDirectory, $"{PACKAGE_NAME}_{version}.hash");
            if (!File.Exists(hashFile) || string.IsNullOrWhiteSpace(File.ReadAllText(hashFile)))
                return false;

            foreach (string file in Directory.GetFiles(outputPackageDirectory))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) || (name.Length == 32 && !name.Contains(".")))
                    return true;
            }

            return false;
        }

        public static bool ValidateBuildResult(string outputPackageDirectory, string version)
        {
            if (!Directory.Exists(outputPackageDirectory))
                return Fail($"构建输出目录不存在: {outputPackageDirectory}");

            string versionFile = Path.Combine(outputPackageDirectory, $"{PACKAGE_NAME}.version");
            if (!File.Exists(versionFile))
                return Fail($"版本文件不存在: {versionFile}");

            string fileVersion = File.ReadAllText(versionFile).Trim();
            if (!string.Equals(fileVersion, version, StringComparison.Ordinal))
                return Fail($"版本不匹配，期望 {version}，实际 {fileVersion}");

            string manifestFile = Path.Combine(outputPackageDirectory, $"{PACKAGE_NAME}_{version}.bytes");
            if (!File.Exists(manifestFile))
                return Fail($"清单文件不存在: {manifestFile}");
            if (new FileInfo(manifestFile).Length == 0)
                return Fail($"清单文件为空: {manifestFile}");

            string hashFile = Path.Combine(outputPackageDirectory, $"{PACKAGE_NAME}_{version}.hash");
            if (!File.Exists(hashFile))
                return Fail($"hash 文件不存在: {hashFile}");
            if (string.IsNullOrWhiteSpace(File.ReadAllText(hashFile)))
                return Fail($"hash 文件为空: {hashFile}");

            bool hasBundle = false;
            foreach (string file in Directory.GetFiles(outputPackageDirectory))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) || (name.Length == 32 && !name.Contains(".")))
                {
                    hasBundle = true;
                    break;
                }
            }

            if (!hasBundle)
                return Fail($"没有找到 bundle 文件: {outputPackageDirectory}");

            Debug.Log($"[YooAssetBuildUtility] 资源包校验通过：{outputPackageDirectory}，version={version}");
            return true;
        }

        private static bool Fail(string message)
        {
            Debug.LogError($"[YooAssetBuildUtility] 资源包校验失败：{message}");
            return false;
        }
    }
}
#endif
