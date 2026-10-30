#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
namespace Game.Editor
{
    /// <summary>冻结 Android 快速包底层证据并拒绝新增原生需求。</summary>
    public static class AndroidQuickAotUtility
    {
        /// <summary>保存 Game 参照和元数据名单，已有记录不得覆盖。</summary>
        public static void Preserve(string directory)
        {
            CopyOnce(Path.Combine(HybridCLRBuildUtility.GetGameDllDirectory(), "Game.dll.bytes"), Path.Combine(directory, "baseline-game.bytes"));
            CopyOnce(HybridCLRBuildUtility.GetRuntimeListPath(), Path.Combine(directory, "runtime-list.bytes"));
        }
        /// <summary>已有文件必须一致。</summary>
        private static void CopyOnce(string source, string target)
        {
            if (!File.Exists(target)) File.Copy(source, target);
            else if (!File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target))) throw new InvalidOperationException("冻结 AOT 参照不允许覆盖。");
        }
        /// <summary>包含非 Game 代码、插件、编译布局和生成桥接。</summary>
        public static string HostFingerprint()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) throw new InvalidOperationException("复用 AOT 必须在 Android 平台。");
            string root = Directory.GetParent(Application.dataPath).FullName;
            var sources = CompilationPipeline.GetAssemblies(AssembliesType.Player).Where(a => a.name != "Game")
                .SelectMany(a => a.sourceFiles.Concat(a.compiledAssemblyReferences.Where(p => !p.Replace('\\', '/').Contains("/Library/"))));
            var assets = Directory.GetFiles(Application.dataPath, "*", SearchOption.AllDirectories).Where(p =>
                p.Replace('\\', '/').Contains("/Plugins/") || p.Replace('\\', '/').Contains("/HybridCLRGenerate/") ||
                new[] { ".asmdef", ".asmref", ".rsp" }.Contains(Path.GetExtension(p)));
            assets = assets.Concat(Directory.GetFiles(Path.Combine(root, "ProjectSettings"), "*", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p) != "ProjectSettings.asset"));
            return AndroidBootBuildGuard.ComputeFingerprint(root) + "|" + AndroidQuickBuildUtility.FingerprintFiles(sources.Concat(assets)
                .Concat(CollectHostAssetInputs(root, AssetDatabase.GetDependencies("Assets/Game/Scenes/Boot.unity", true)))
                .SelectMany(p => File.Exists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p })
                .Concat(Directory.GetFiles(Path.GetFullPath(HybridCLR.Editor.SettingsUtil.GeneratedCppDir), "*", SearchOption.AllDirectories))
                .Concat(new[] { Path.Combine(root,"Packages","manifest.json"), Path.Combine(root,"Packages","packages-lock.json") }).ToArray());
        }
        /// <summary>只收集直接进入宿主的资源，不把普通远程美术冻结为 AOT 输入。</summary>
        public static string[] CollectHostAssetInputs(string root, string[] bootDependencies)
        {
            string assets = Path.Combine(root, "Assets");
            return Directory.GetFiles(assets, "*", SearchOption.AllDirectories).Where(path =>
            {
                string relative = Path.GetRelativePath(assets, path).Replace('\\', '/');
                bool resources = relative.StartsWith("Resources/", StringComparison.Ordinal) || relative.Contains("/Resources/");
                bool streaming = relative.StartsWith("StreamingAssets/", StringComparison.Ordinal) &&
                    !relative.StartsWith("StreamingAssets/yoo/", StringComparison.Ordinal) && relative != "StreamingAssets/yoo.meta" &&
                    relative != "StreamingAssets/windows-release.json" && relative != "StreamingAssets/windows-release.json.meta";
                return resources || streaming;
            }).Concat(bootDependencies.Where(File.Exists)).SelectMany(path => File.Exists(path + ".meta") ? new[] { path, path + ".meta" } : new[] { path })
                .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>新增外部泛型或原生入口无法证明桥接兼容时要求完整准备。</summary>
        public static void ValidateGame(string baseline, string current)
        {
            if (File.ReadAllBytes(baseline).SequenceEqual(File.ReadAllBytes(current))) return;
            PcHotUpdateBaseline.ValidateManagedReferences(baseline, current);
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(current)))
                foreach (var method in module.GetTypes().SelectMany(t => t.Methods))
                    if (method.IsPinvokeImpl || method.CustomAttributes.Any(a => a.TypeFullName.Contains("MonoPInvokeCallback") || a.TypeFullName.Contains("UnmanagedCallersOnly")) ||
                        (method.HasBody && method.Body.Instructions.Any(i => i.OpCode == OpCodes.Calli)))
                        throw new InvalidOperationException("Game 存在原生调用或回调需求，必须重新完整准备 AOT。");
        }
    }
}
#endif
