#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
namespace Game.Editor
{
    /// <summary>Player前保存可信准备记录；失败重试不依赖成功Player状态。</summary>
    internal static class PcQuickBuildUtility
    {
        [Serializable] internal sealed class Prepared
        {
            public int Schema = 2; // 记录格式。
            public string Platform = "StandaloneWindows64"; // 固定平台身份。
            public string Version, Kind, Package, Directory; // 本轮版本、包型和独立归档。
            public string Source, Host, Generated, Runtime, Products, Frozen; // 各阶段内容指纹。
        }
        private static string Root => System.IO.Directory.GetParent(Application.dataPath).FullName;
        private static string Cache => Path.Combine(Root, "Build", "PcPrepared");
        /// <summary>所有资源源文件及设置参与检查，排除正常生成和暂存目录。</summary>
        internal static string Sources() => Fingerprint(new[] { "Assets", "Packages", "ProjectSettings" }
            .SelectMany(folder => System.IO.Directory.GetFiles(Path.Combine(Root, folder), "*", SearchOption.AllDirectories))
            .Where(path => !path.Replace('\\','/').Contains("/StreamingAssets/") &&
                !path.Replace('\\','/').Contains("/HybridCLRGenerate/") &&
                !path.Replace('\\','/').EndsWith("/HybridCLRGenerate.meta", StringComparison.Ordinal) &&
                !path.Replace('\\','/').EndsWith("/StreamingAssets.meta", StringComparison.Ordinal) &&
                (!path.Replace('\\','/').Contains("/Editor/") || path.Replace('\\','/').Contains("/Editor/YooAsset/")) &&
                !path.Replace('\\','/').Contains("/Tests/") &&
                !path.StartsWith(HybridCLRBuildUtility.GetGameDllDirectory(), StringComparison.OrdinalIgnoreCase)).ToArray());
        /// <summary>桥接及链接生成资料必须保持为同一批次。</summary>
        internal static string Generated() => Fingerprint(new[] {
            Path.GetFullPath(HybridCLR.Editor.SettingsUtil.GeneratedCppDir), Path.Combine(Root, "Assets", "HybridCLRGenerate") }
            .SelectMany(path => System.IO.Directory.GetFiles(path, "*", SearchOption.AllDirectories)).ToArray());
        /// <summary>按路径及内容识别增删、重命名和同长度变更。</summary>
        internal static string Fingerprint(string[] files)
        {
            using (var sha = SHA256.Create())
            using (var content = new MemoryStream())
            using (var writer = new BinaryWriter(content, Encoding.UTF8, true))
            {
                foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal))
                {
                    writer.Write(file);
                    using (var input = File.OpenRead(file)) writer.Write(sha.ComputeHash(input));
                }
                writer.Flush(); content.Position = 0;
                return Convert.ToBase64String(sha.ComputeHash(content));
            }
        }
        /// <summary>归档记录本身不参与自身的内容指纹。</summary>
        private static string Tree(string directory) => Fingerprint(System.IO.Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "prepared.json").ToArray());
        /// <summary>同时绑定下载资源和待打入Player的内置资源。</summary>
        private static string Products(Prepared p) => Tree(p.Package) + Tree(YooAssetBuildUtility.GetStreamingAssetsDirectory());
        /// <summary>资源完成后、Player开始前冻结AOT与配套Game，记录只写新目录。</summary>
        internal static Prepared Save(string version, string kind, string package, string expectedSource, string expectedHost, string expectedRuntime, string expectedGenerated)
        {
            RequireUnchangedPreparation(expectedRuntime, PcHotUpdateBaseline.CaptureRuntimeDllInputs(), expectedGenerated, Generated());
            if (Sources() != expectedSource || PcHotUpdateBaseline.CaptureCurrentInputs() != expectedHost)
                throw new InvalidOperationException("准备过程中源码、资源或宿主设置变化，请重新完整准备。");
            string directory = Path.Combine(Cache, Guid.NewGuid().ToString("N"));
            ValidateIdentity(new Prepared { Version = version, Kind = kind, Package = package, Directory = directory }, Cache, YooAssetBuildUtility.GetPackageRoot(UnityEditor.BuildTarget.StandaloneWindows64));
            System.IO.Directory.CreateDirectory(Path.Combine(directory, "Aot"));
            string dlls = HybridCLRBuildUtility.GetGameDllDirectory();
            string list = Path.Combine(dlls, "HybridCLRAssemblyList.bytes");
            foreach (string name in File.ReadAllLines(list).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()))
            {
                if (Path.GetFileName(name) != name || !name.EndsWith(".dll", StringComparison.Ordinal)) throw new InvalidOperationException("AOT清单非法。");
                File.Copy(Path.Combine(dlls, name + ".bytes"), Path.Combine(directory, "Aot", name));
            }
            File.Copy(list, Path.Combine(directory, "HybridCLRAssemblyList.bytes"));
            File.Copy(Path.Combine(dlls, "Game.dll.bytes"), Path.Combine(directory, "Game.dll"));
            // 兼容现有原生与托管引用校验器，仅提供其所需Game指纹。
            using (var sha = SHA256.Create())
                File.WriteAllText(Path.Combine(directory, "baseline.json"), "{\"GameHash\":\"" + Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(Path.Combine(directory, "Game.dll")))) + "\"}");
            var p = new Prepared { Version = version, Kind = kind, Package = package, Directory = directory,
                Source = expectedSource, Host = expectedHost, Generated = expectedGenerated, Runtime = expectedRuntime };
            PcBuiltinResourceValidator.CaptureRemote(package, version);
            Builtin(p);
            p.Products = Products(p); p.Frozen = Tree(directory);
            string json = JsonUtility.ToJson(p, true);
            File.WriteAllText(Path.Combine(directory, "prepared.json"), json);
            RequireUnchangedPreparation(expectedRuntime, PcHotUpdateBaseline.CaptureRuntimeDllInputs(), expectedGenerated, Generated());
            Validate(p, true);
            // 只在新归档完整落盘后原子替换索引，写入中断不破坏上一批指针。
            string latest = Path.Combine(Cache, "latest.json");
            string pending = latest + ".pending-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(pending, json);
                if (File.Exists(latest)) File.Replace(pending, latest, null);
                else File.Move(pending, latest);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            return p;
        }
        /// <summary>资源构建前后的DLL与桥接输入必须一致，不能登记中途变化的组合。</summary>
        internal static void RequireUnchangedPreparation(string runtimeBefore, string runtimeAfter, string generatedBefore, string generatedAfter)
        {
            if (string.IsNullOrEmpty(runtimeBefore) || string.IsNullOrEmpty(generatedBefore) || runtimeBefore != runtimeAfter || generatedBefore != generatedAfter)
                throw new InvalidOperationException("资源准备期间DLL或桥接输入变化，请重新准备。");
        }
        /// <summary>读取最新准备索引后先验证，缺失记录时不推断旧文件可用。</summary>
        internal static Prepared Load(bool reuseAll)
        {
            string latest = Path.Combine(Cache, "latest.json");
            if (!File.Exists(latest))
                throw new InvalidOperationException("缺少可信的PC准备记录，请先执行完整打包或启动包，完成代码和资源准备后再快速打包。");
            var p = JsonUtility.FromJson<Prepared>(File.ReadAllText(latest));
            Validate(p, reuseAll);
            return p;
        }
        /// <summary>拒绝改动、损坏、旧格式及另一平台记录；AOT模式允许普通玩法与远程资源变化。</summary>
        internal static void Validate(Prepared p, bool reuseAll)
        {
            ValidateIdentity(p, Cache, YooAssetBuildUtility.GetPackageRoot(UnityEditor.BuildTarget.StandaloneWindows64));
            if (File.ReadAllText(Path.Combine(p.Directory, "prepared.json")) != JsonUtility.ToJson(p, true))
                throw new InvalidOperationException("PC准备索引与不可变记录不匹配。");
            if (
                p.Host != PcHotUpdateBaseline.CaptureCurrentInputs() || p.Generated != Generated() || p.Frozen != Tree(p.Directory))
                throw new InvalidOperationException("PC准备基线不兼容或已损坏，请完整打包。");
            if (reuseAll && (p.Source != Sources() || p.Runtime != PcHotUpdateBaseline.CaptureRuntimeDllInputs() || p.Products != Products(p)))
                throw new InvalidOperationException("代码、资源或生成结果已变化，不能复用代码和资源。");
        }
        /// <summary>先验证版本与目录身份，禁止旧记录、跨平台和路径穿越读取。</summary>
        internal static void ValidateIdentity(Prepared p, string cache, string packageRoot)
        {
            if (p == null || p.Schema != 2 || p.Platform != "StandaloneWindows64" ||
                (p.Kind != "Full" && p.Kind != "SmallHostBase") || string.IsNullOrEmpty(p.Version) ||
                p.Version.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-') ||
                string.IsNullOrEmpty(p.Directory) || string.IsNullOrEmpty(p.Package))
                throw new InvalidOperationException("PC准备记录身份无效。");
            string name = Path.GetFileName(p.Directory);
            if (!Guid.TryParseExact(name, "N", out _) ||
                !string.Equals(Path.GetFullPath(p.Directory), Path.GetFullPath(Path.Combine(cache, name)), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFullPath(p.Package), Path.GetFullPath(Path.Combine(packageRoot, p.Version)), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PC准备记录目录不属于本工程对应版本。");
        }
        /// <summary>沿用记录包型检查完整内置或仅清单内置，不自动切换类型。</summary>
        internal static PcBuiltinResourceValidator Builtin(Prepared p) => p.Kind == "Full"
            ? PcBuiltinResourceValidator.Capture(YooAssetBuildUtility.GetStreamingAssetsDirectory(), p.Version)
            : PcBuiltinResourceValidator.CaptureStartup(YooAssetBuildUtility.GetStreamingAssetsDirectory(), p.Package, p.Version);
    }
}
#endif
