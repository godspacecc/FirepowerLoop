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

            PrepareDllTargetDirectory();
            CopyGameDll(hotUpdateDir);

            var copiedAot = new List<string>();
            foreach (string dllName in aotAssemblies)
            {
                string sourcePath = Path.Combine(aotDir, dllName);
                if (!File.Exists(sourcePath))
                {
                    string hint = requireExistingPlayerAot
                        ? "请先执行最新代码资源完整打包以生成完整 Player 的 AOT DLL。"
                        : "请检查 HybridCLR 生成和预备 Player 是否成功。";
                    throw new FileNotFoundException($"AOT DLL 不存在: {sourcePath}\n{hint}");
                }

                File.Copy(sourcePath, Path.Combine(DLLS_TARGET_DIR, dllName + ".bytes"), true);
                copiedAot.Add(dllName);
            }

            GenerateAotListFile(copiedAot);
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

            PrepareDllTargetDirectory();
            CopyGameDll(GetHotUpdateDllDirectory(target));

            foreach (string dllName in aotAssemblies)
            {
                string sourcePath = Path.Combine(baselineAotDirectory, dllName);
                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException($"AOT 基线缺少 DLL: {sourcePath}");
                File.Copy(sourcePath, Path.Combine(DLLS_TARGET_DIR, dllName + ".bytes"), true);
            }

            GenerateAotListFile(aotAssemblies);
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

        private static void PrepareDllTargetDirectory()
        {
            if (!Directory.Exists(DLLS_TARGET_DIR))
                Directory.CreateDirectory(DLLS_TARGET_DIR);

            foreach (string file in Directory.GetFiles(DLLS_TARGET_DIR, "*.bytes"))
                File.Delete(file);
            foreach (string file in Directory.GetFiles(DLLS_TARGET_DIR, "*.bytes.meta"))
                File.Delete(file);
        }

        private static void CopyGameDll(string hotUpdateDir)
        {
            string source = Path.Combine(hotUpdateDir, "Game.dll");
            if (!File.Exists(source))
                throw new FileNotFoundException($"Game.dll 不存在: {source}");
            File.Copy(source, Path.Combine(DLLS_TARGET_DIR, "Game.dll.bytes"), true);
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

        private static void GenerateAotListFile(IEnumerable<string> aotAssemblies)
        {
            string listPath = Path.Combine(DLLS_TARGET_DIR, AOT_LIST_FILE);
            File.WriteAllLines(listPath, aotAssemblies, Utf8NoBom);
            Debug.Log($"[HybridCLRBuildUtility] 已生成 {AOT_LIST_FILE}");
        }
    }
}
#endif
