#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 支撑编辑器构建流程中的 HybridCLRBuildUtility，记录或执行构建、资源和本地服务相关职责。
    /// </summary>
    public static class HybridCLRBuildUtility
    {
        private const string DLLS_TARGET_DIR = "Assets/Game/Dlls";
        private const string AOT_LIST_FILE = "HybridCLRAssemblyList.bytes";
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void GenerateAll()
        {
            Debug.Log("[HybridCLRBuildUtility] 开始 GenerateAll。");
            PrebuildCommand.GenerateAll();
            Debug.Log("[HybridCLRBuildUtility] GenerateAll 完成。");
        }

        public static void GeneratePrePlayer(BuildTarget target)
        {
            Debug.Log("[HybridCLRBuildUtility] 开始 GeneratePrePlayer。");
            CompileHotUpdateDll(target);
            Il2CppDefGeneratorCommand.GenerateIl2CppDef();
            LinkGeneratorCommand.GenerateLinkXml(target);
            Debug.Log("[HybridCLRBuildUtility] GeneratePrePlayer 完成。");
        }

        public static void GeneratePostPlayer(BuildTarget target)
        {
            Debug.Log("[HybridCLRBuildUtility] 开始 GeneratePostPlayer。");
            StripAOTDllCommand.GenerateStripedAOTDlls(target);
            MethodBridgeGeneratorCommand.GenerateMethodBridgeAndReversePInvokeWrapper(target);
            AOTReferenceGeneratorCommand.GenerateAOTGenericReference(target);
            Debug.Log("[HybridCLRBuildUtility] GeneratePostPlayer 完成。");
        }

        /// <summary>
        /// 编译热更新 DLL；无论编辑器勾选状态如何都固定使用非 Development 参数，保持诊断正式版基线一致。
        /// </summary>
        public static void CompileHotUpdateDll(BuildTarget target)
        {
            Debug.Log($"[HybridCLRBuildUtility] 编译热更新 DLL：{target}，development=False");
            CompileDllCommand.CompileDll(target, false);
        }

        public static IReadOnlyList<string> CopyGeneratedDlls(BuildTarget target, bool requireExistingPlayerAot)
        {
            var aotAssemblies = ReadPatchedAotAssemblyList();
            if (aotAssemblies == null || aotAssemblies.Count == 0)
                throw new InvalidOperationException("AOT 清单为空或无法读取。");

            aotAssemblies = NormalizeDllList(aotAssemblies).ToList();
            string hotUpdateDir = GetHotUpdateDllDirectory(target);
            string aotDir = GetAotDirectory(target);
            if (!Directory.Exists(aotDir))
                throw new DirectoryNotFoundException($"AOT 目录不存在: {aotDir}");

            // 发布前统一检查所有源文件，避免缺少 AOT 时破坏上次产物。
            var copiedAot = aotAssemblies;
            PublishDllFiles(hotUpdateDir, aotDir, copiedAot.ToArray(), DLLS_TARGET_DIR);
            AssetDatabase.Refresh();

            var allCopied = new List<string> { "Game.dll" };
            allCopied.AddRange(copiedAot);
            Debug.Log($"[HybridCLRBuildUtility] 已复制 DLL：{allCopied.Count}");
            return allCopied.AsReadOnly();
        }

        public static IReadOnlyList<string> CopyGeneratedDllsFromBaseline(BuildTarget target, string baselineAotDirectory, string baselineRuntimeListPath)
        {
            if (!File.Exists(baselineRuntimeListPath))
                throw new FileNotFoundException($"AOT 基线清单不存在: {baselineRuntimeListPath}");
            if (!Directory.Exists(baselineAotDirectory))
                throw new DirectoryNotFoundException($"AOT 基线目录不存在: {baselineAotDirectory}");

            var aotAssemblies = NormalizeDllList(File.ReadAllLines(baselineRuntimeListPath, Utf8NoBom)).ToList();
            if (aotAssemblies.Count == 0)
                throw new InvalidOperationException($"AOT 基线清单为空: {baselineRuntimeListPath}");

            PublishDllFiles(GetHotUpdateDllDirectory(target), baselineAotDirectory, aotAssemblies.ToArray(), DLLS_TARGET_DIR);
            AssetDatabase.Refresh();

            var allCopied = new List<string> { "Game.dll" };
            allCopied.AddRange(aotAssemblies);
            return allCopied.AsReadOnly();
        }

        public static void ValidateGeneratedDlls()
        {
            if (!Directory.Exists(DLLS_TARGET_DIR))
                throw new InvalidOperationException($"DLL 目录不存在: {DLLS_TARGET_DIR}");

            string gameDll = Path.Combine(DLLS_TARGET_DIR, "Game.dll.bytes");
            if (!File.Exists(gameDll))
                throw new InvalidOperationException($"Game.dll.bytes 不存在: {gameDll}");

            string listPath = Path.Combine(DLLS_TARGET_DIR, AOT_LIST_FILE);
            if (!File.Exists(listPath))
                throw new InvalidOperationException($"AOT 清单不存在: {listPath}");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dllName in NormalizeDllList(File.ReadAllLines(listPath, Utf8NoBom)))
            {
                if (!seen.Add(dllName))
                    throw new InvalidOperationException($"AOT 清单重复: {dllName}");
                if (dllName.Equals("Game.dll", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("AOT 清单不应包含 Game.dll。");
                string bytesPath = Path.Combine(DLLS_TARGET_DIR, dllName + ".bytes");
                if (!File.Exists(bytesPath))
                    throw new InvalidOperationException($"AOT bytes 不存在: {bytesPath}");
            }

            if (seen.Count == 0)
                throw new InvalidOperationException("AOT 清单为空。");

            Debug.Log($"[HybridCLRBuildUtility] DLL 校验通过，AOT DLL 数量：{seen.Count}");
        }

        public static string GetAotDirectory(BuildTarget target)
        {
            string dir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            if (Directory.Exists(dir))
                return Path.GetFullPath(dir);

            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectPath, "HybridCLRData", "AssembliesPostIl2CppStrip", target.ToString()));
        }

        public static string GetRuntimeListPath()
        {
            return Path.GetFullPath(Path.Combine(DLLS_TARGET_DIR, AOT_LIST_FILE));
        }

        public static string GetGameDllDirectory()
        {
            return Path.GetFullPath(DLLS_TARGET_DIR);
        }

        private static string GetHotUpdateDllDirectory(BuildTarget target)
        {
            string projectPath = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectPath, "HybridCLRData", "HotUpdateDlls", target.ToString()));
        }

        /// <summary>
        /// 发布 DLL 内容并保留现用资源的 meta；专用 DLL 目录内的 .dll.bytes 为生成产物。
        /// 完整预检源文件后才更改输出，退役 DLL 连同 meta 删除，无关文件保持原样。
        /// </summary>
        internal static void PublishDllFiles(string hotUpdateDir, string aotDir, string[] aotAssemblies, string targetDirectory)
        {
            var normalized = NormalizeDllList(aotAssemblies).ToArray();
            if (normalized.Length == 0)
                throw new InvalidOperationException("AOT 清单为空。");
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Game.dll.bytes", Path.Combine(hotUpdateDir, "Game.dll") }
            };
            foreach (string dllName in normalized)
            {
                // 清单条目必须是简单文件名，不能把发布写入目标目录之外。
                if (dllName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                    throw new InvalidOperationException($"AOT DLL 名称无效: {dllName}");
                sources.Add(dllName + ".bytes", Path.Combine(aotDir, dllName));
            }
            foreach (string source in sources.Values)
                if (!File.Exists(source))
                    throw new FileNotFoundException($"DLL 不存在: {source}，请检查 HybridCLR 生成或 Player AOT 基线。", source);

            Directory.CreateDirectory(targetDirectory);
            foreach (var source in sources)
                File.Copy(source.Value, Path.Combine(targetDirectory, source.Key), true);
            File.WriteAllLines(Path.Combine(targetDirectory, AOT_LIST_FILE), normalized, Utf8NoBom);

            foreach (string file in Directory.GetFiles(targetDirectory))
            {
                string name = Path.GetFileName(file);
                string assetName = name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(0, name.Length - 5) : name;
                if (assetName.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase) && !sources.ContainsKey(assetName))
                    File.Delete(file);
            }
        }

        private static IEnumerable<string> NormalizeDllList(IEnumerable<string> values)
        {
            return values
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrEmpty(value) && !value.StartsWith("//"))
                .Select(value => value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? value : value + ".dll")
                .Where(value => !value.Equals("Game.dll", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        }

        private static List<string> ReadPatchedAotAssemblyList()
        {
            string aotReferencesPath = "Assets/HybridCLRGenerate/AOTGenericReferences.cs";
            if (!File.Exists(aotReferencesPath))
                return null;

            string content = File.ReadAllText(aotReferencesPath);
            int start = content.IndexOf("PatchedAOTAssemblyList", StringComparison.Ordinal);
            if (start < 0)
                return null;

            int braceStart = content.IndexOf('{', start);
            int braceEnd = content.IndexOf('}', braceStart);
            if (braceStart < 0 || braceEnd < 0)
                return null;

            string listContent = content.Substring(braceStart, braceEnd - braceStart + 1);
            var matches = System.Text.RegularExpressions.Regex.Matches(listContent, "\"([^\"]+)\"");
            return matches.Cast<System.Text.RegularExpressions.Match>()
                .Select(match => match.Groups[1].Value)
                .Where(value => value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

    }
}
#endif
