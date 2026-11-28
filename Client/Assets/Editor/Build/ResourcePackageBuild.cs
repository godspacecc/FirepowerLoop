using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Shared.UnityTools
{
    /// <summary>执行统一资源构建，并对本次产物输出完整性和模块体积报告。</summary>
    internal static class ResourcePackageBuild
    {
        internal const string Menu = "Tools/打包/资源/生成资源包";
        internal static string StandaloneOutputRoot => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../..", "Backup", "Artifacts", "UnityTools", "StandalonePackages"));

        [Serializable]
        private sealed class Manifest
        {
            public string PackageName, PackageVersion;
            public int OutputNameStyle;
            public Bundle[] BundleList;
            public Asset[] AssetList;
        }

        [Serializable]
        private sealed class Bundle
        {
            public string BundleName, FileHash;
            public long FileSize;
            public int[] DependBundleIDs;
        }

        [Serializable]
        private sealed class Asset
        {
            public string AssetPath;
            public string[] AssetTags;
            public int BundleID;
            public int[] DependBundleIDs;
        }

        internal static BuildResult Run(ScriptableBuildParameters parameters)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("请在空闲的编辑模式生成资源包。");
            var result = new ScriptableBuildPipeline().Run(parameters, true);
            if (result.Success)
                WriteReports(result.OutputPackageDirectory, parameters.PackageName, parameters.PackageVersion, parameters.BuildTarget.ToString());
            return result;
        }

        internal static void WriteReports(string directory, string package, string version, string platform)
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", "Backup", "Artifacts",
                "UnityTools", "Resources", platform, package, version));
            Directory.CreateDirectory(output);
            var issues = new List<string>();
            Manifest manifest = Inspect(directory, package, version, issues);
            if (manifest != null)
                WriteModuleSizes(output, platform, package, version, manifest, issues);
            int count = manifest?.BundleList.Length ?? 0;
            File.WriteAllText(Path.Combine(output, "integrity.json"), JsonConvert.SerializeObject(
                new { platform, package, version, complete = issues.Count == 0, files = count, issues }, Formatting.Indented));
            if (issues.Count != 0)
                throw new InvalidDataException($"资源包完整性校验失败，共 {issues.Count} 项。报告：{output}");
            Debug.Log($"[资源包] {platform} {version} 完整性和模块体积报告：{output}");
        }

        internal static bool IsComplete(string directory, string package, string version)
        {
            var issues = new List<string>();
            return Inspect(directory, package, version, issues) != null && issues.Count == 0;
        }

        private static Manifest Inspect(string directory, string package, string version, List<string> issues)
        {
            string stem = package + "_" + version;
            try
            {
                string versionFile = Path.Combine(directory, package + ".version");
                if (!File.Exists(versionFile) || File.ReadAllText(versionFile).Trim() != version)
                    issues.Add("资源包版本文件缺失或版本不一致。");
                string binary = Path.Combine(directory, stem + ".bytes");
                string checksum = Path.Combine(directory, stem + ".hash");
                if (!File.Exists(binary) || !File.Exists(checksum) ||
                    !string.Equals(File.ReadAllText(checksum).Trim(), HashUtility.FileCRC32(binary), StringComparison.OrdinalIgnoreCase))
                    issues.Add("二进制清单缺失或校验失败。");
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(directory, stem + ".json")));
                if (manifest?.BundleList == null || manifest.BundleList.Length == 0 || manifest.AssetList == null ||
                    manifest.PackageName != package || manifest.PackageVersion != version)
                    throw new InvalidDataException("资源包清单结构或身份无效。");
                foreach (var bundle in manifest.BundleList)
                {
                    string name = BundleFileName(manifest.OutputNameStyle, bundle);
                    if (name != Path.GetFileName(name) || string.IsNullOrWhiteSpace(name))
                    {
                        issues.Add("资源文件名无效：" + name);
                        continue;
                    }

                    var file = new FileInfo(Path.Combine(directory, name));
                    if (!file.Exists || file.Length != bundle.FileSize ||
                        !string.Equals(HashUtility.FileMD5(file.FullName), bundle.FileHash, StringComparison.OrdinalIgnoreCase))
                        issues.Add("资源文件缺失或内容不一致：" + name);
                }
                return manifest;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException ||
                                          error is UnauthorizedAccessException || error is ArgumentException ||
                                          error is InvalidOperationException || error is NullReferenceException)
            {
                issues.Add(error.GetBaseException().Message);
                return null;
            }
        }

        private static void WriteModuleSizes(string output, string platform, string package, string version,
            Manifest manifest, List<string> issues)
        {
            var groups = AssetBundleCollectorSettingData.Setting.Packages.FirstOrDefault(item => item.PackageName == package)?.Groups;
            var collectorPaths = groups?.SelectMany(group => group.Collectors.Select(collector =>
                    (group.GroupName, Path: collector.CollectPath?.TrimEnd('/'))))
                .Where(item => !string.IsNullOrWhiteSpace(item.Path)).ToArray();
            var modules = new SortedDictionary<string, HashSet<int>>(StringComparer.Ordinal);
            int count = manifest.BundleList.Length;
            foreach (var asset in manifest.AssetList)
            {
                var labels = (asset.AssetTags ?? Array.Empty<string>()).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToArray();
                if (labels.Length == 0 && !string.IsNullOrEmpty(asset.AssetPath) && collectorPaths != null)
                    labels = collectorPaths.Where(item => asset.AssetPath == item.Path ||
                            asset.AssetPath.StartsWith(item.Path + "/", StringComparison.Ordinal))
                        .Select(item => item.GroupName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
                foreach (string label in labels)
                {
                    if (!modules.TryGetValue(label, out var ids))
                        modules[label] = ids = new HashSet<int>();
                    AddBundle(asset.BundleID, ids);
                    foreach (int dependency in asset.DependBundleIDs ?? Array.Empty<int>())
                        AddBundle(dependency, ids);
                }
            }
            var tagged = new HashSet<int>(modules.Values.SelectMany(ids => ids));
            modules["（未标记模块）"] = Enumerable.Range(0, count).Where(id => !tagged.Contains(id)).ToHashSet();
            var sizes = modules.Select(module => new
            {
                module = module.Key, files = module.Value.Count,
                bytes = module.Value.Sum(bundle => manifest.BundleList[bundle].FileSize)
            }).ToArray();
            long total = manifest.BundleList.Sum(bundle => bundle.FileSize);
            File.WriteAllText(Path.Combine(output, "module-sizes.json"), JsonConvert.SerializeObject(
                new { platform, package, version, total, sharedBundlesCanAppearInMultipleModules = true, modules = sizes }, Formatting.Indented));

            void AddBundle(int id, HashSet<int> set)
            {
                if (id < 0 || id >= count) { issues.Add("清单包含无效 Bundle 索引：" + id); return; }
                if (!set.Add(id)) return;
                foreach (int dependency in manifest.BundleList[id].DependBundleIDs ?? Array.Empty<int>())
                    AddBundle(dependency, set);
            }
        }

        private static string BundleFileName(int style, Bundle bundle)
        {
            string extension = Path.GetExtension(bundle.BundleName);
            return (EFileNameStyle)style switch
            {
                EFileNameStyle.HashName => bundle.FileHash + extension,
                EFileNameStyle.BundleName => bundle.BundleName,
                EFileNameStyle.BundleName_HashName => Path.GetFileNameWithoutExtension(bundle.BundleName) + "_" + bundle.FileHash + extension,
                _ => throw new InvalidDataException("资源文件命名方式未知。")
            };
        }
    }
}
