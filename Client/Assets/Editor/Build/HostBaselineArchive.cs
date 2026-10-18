#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Game.Editor
{
    /// <summary>独立、不可覆盖的宿主归档；所有载荷按相对路径和SHA256验证，搬迁后重新定位状态，不读取旧Library。</summary>
    public static class HostBaselineArchive
    {
        [Serializable] public sealed class Entry { public string Path, Sha256; public long Size; }
        [Serializable] public sealed class Manifest
        {
            public int SchemaVersion = 1;
            public string ProjectId = "FirepowerLoop", Platform, Commit, UnityVersion, BootFingerprint;
            public List<Entry> Files = new List<Entry>();
        }

        /// <summary>只归档选定平台成功首包及生成输入；先在同父目录暂存验证，最后发布为新目录。</summary>
        public static void Create(string destination, string platform, string commit, string unityVersion, string bootFingerprint, string stateJson, string generatedDirectory)
        {
            RequireNewDestination(destination);
            CheckIdentity(platform, commit, unityVersion, bootFingerprint);
            var state = JsonConvert.DeserializeObject<BuildState>(stateJson) ?? throw new InvalidOperationException("宿主状态为空");
            var aot = platform == "Android" ? state.LastAndroidAotBaseline : state.LastAotBaseline;
            var host = platform == "Android" ? state.LastAndroidHost : state.LastHost;
            var package = platform == "Android" ? state.LastAndroidPackage : state.LastPackage;
            var player = platform == "Android" ? state.LastAndroidPlayer : state.LastPlayer;
            string target = platform == "Android" ? "Android" : "StandaloneWindows64";
            if (aot == null || host == null || package == null || player == null || aot.BuildTarget != target || host.BuildTarget != target || package.BuildTarget != target ||
                host.Version != package.Version || player.Version != package.Version || string.IsNullOrWhiteSpace(package.Version) || package.BuildKind != "SmallHostBase")
                throw new InvalidOperationException("只能归档同平台同版本的成功首包");
            bool valid = platform == "Android" ? BuildStateStore.TryValidateAndroidAotBaseline(state, UnityEditor.BuildTarget.Android, out _) :
                BuildStateStore.TryValidateAotBaseline(state, UnityEditor.BuildTarget.StandaloneWindows64, out _);
            if (!valid || !File.Exists(player.PlayerPath)) throw new InvalidOperationException("首包AOT或Player缺失");
            if (platform == "Android" && player.BootFingerprint != bootFingerprint) throw new InvalidOperationException("首包Boot指纹不符");
            string staging = Path.GetFullPath(destination) + ".pending-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(staging);
                CopyTree(aot.AotDirectory, Path.Combine(staging, "Aot"));
                CopyTree(aot.GameDllDirectory, Path.Combine(staging, "Dlls"));
                CopyTree(package.OutputPackageDirectory, Path.Combine(staging, "Package"));
                CopyTree(generatedDirectory, Path.Combine(staging, "Generated"));
                string playerName = Path.GetFileName(player.PlayerPath);
                if (platform == "PC") CopyTree(Path.GetDirectoryName(player.PlayerPath), Path.Combine(staging, "Player"));
                else { Directory.CreateDirectory(Path.Combine(staging, "Player")); WorkspaceBuildLock.EnsureNoLinks(player.PlayerPath); File.Copy(player.PlayerPath, Path.Combine(staging, "Player", playerName)); }
                aot.AotDirectory = "Aot"; aot.RuntimeListPath = "Dlls/HybridCLRAssemblyList.bytes"; aot.GameDllDirectory = "Dlls";
                host.OutputPackageDirectory = "Package"; host.GameDllDirectory = "Dlls"; package.OutputPackageDirectory = "Package";
                player.PlayerPath = "Player/" + playerName;
                var selected = new BuildState();
                if (platform == "Android") { selected.LastAndroidHost = host; selected.LastAndroidAotBaseline = aot; selected.LastAndroidPackage = package; selected.LastAndroidPlayer = player; }
                else { selected.LastHost = host; selected.LastAotBaseline = aot; selected.LastPackage = package; selected.LastPlayer = player; }
                File.WriteAllText(Path.Combine(staging, "state.json"), JsonConvert.SerializeObject(selected, Formatting.Indented));
                var manifest = new Manifest { Platform = platform, Commit = commit, UnityVersion = unityVersion, BootFingerprint = bootFingerprint };
                manifest.Files = TreeFiles(staging).Select(file => new Entry { Path = Relative(staging, file), Size = new FileInfo(file).Length, Sha256 = Hash(file) }).ToList();
                File.WriteAllText(Path.Combine(staging, "baseline.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented));
                Validate(staging, platform, unityVersion, bootFingerprint);
                Directory.Move(staging, destination);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }

        /// <summary>平台、Unity和Boot必须完全一致；多出、缺失、重名、链接及同长度篡改均拒绝。</summary>
        public static void Validate(string directory, string platform, string unityVersion, string bootFingerprint)
        {
            WorkspaceBuildLock.EnsureNoLinks(directory);
            var manifest = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(Path.Combine(directory, "baseline.json")));
            if (manifest == null || manifest.SchemaVersion != 1 || manifest.ProjectId != "FirepowerLoop" || manifest.Platform != platform || manifest.UnityVersion != unityVersion || manifest.BootFingerprint != bootFingerprint)
                throw new InvalidOperationException("宿主归档项目、平台、Unity或Boot身份不符");
            CheckIdentity(manifest.Platform, manifest.Commit, manifest.UnityVersion, manifest.BootFingerprint);
            if (manifest.Files == null || manifest.Files.Count == 0) throw new InvalidOperationException("基线文件清单为空");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in manifest.Files)
            {
                string path = Resolve(directory, entry.Path);
                if (!seen.Add(entry.Path) || entry.Path == "baseline.json" || !File.Exists(path) || new FileInfo(path).Length != entry.Size || Hash(path) != entry.Sha256)
                    throw new InvalidOperationException("宿主归档文件重复、缺失或哈希不符：" + entry.Path);
            }
            var actual = TreeFiles(directory).Select(file => Relative(directory, file)).Where(file => file != "baseline.json").ToList();
            if (actual.Count != seen.Count || actual.Any(file => !seen.Contains(file))) throw new InvalidOperationException("归档存在未列入清单的文件");
            RebaseState(directory, platform);
        }

        /// <summary>恢复到全新目录，二次完整校验后返回仅引用新路径的BuildState JSON；不自动改写工程或Library。</summary>
        public static string Restore(string directory, string destination, string platform, string unityVersion, string bootFingerprint)
        {
            RequireNewDestination(destination); Validate(directory, platform, unityVersion, bootFingerprint);
            string staging = Path.GetFullPath(destination) + ".pending-" + Guid.NewGuid().ToString("N");
            try
            {
                CopyTree(directory, staging); Validate(staging, platform, unityVersion, bootFingerprint);
                Directory.Move(staging, destination);
                return JsonConvert.SerializeObject(RebaseState(destination, platform), Formatting.Indented);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }

        private static BuildState RebaseState(string directory, string platform)
        {
            var state = JsonConvert.DeserializeObject<BuildState>(File.ReadAllText(Path.Combine(directory, "state.json"))) ?? throw new InvalidOperationException("基线状态缺失");
            var aot = platform == "Android" ? state.LastAndroidAotBaseline : state.LastAotBaseline;
            var host = platform == "Android" ? state.LastAndroidHost : state.LastHost;
            var package = platform == "Android" ? state.LastAndroidPackage : state.LastPackage;
            var player = platform == "Android" ? state.LastAndroidPlayer : state.LastPlayer;
            string target = platform == "Android" ? "Android" : "StandaloneWindows64";
            if (aot == null || host == null || package == null || player == null || aot.BuildTarget != target || host.BuildTarget != target || package.BuildTarget != target ||
                aot.AotDirectory != "Aot" || aot.RuntimeListPath != "Dlls/HybridCLRAssemblyList.bytes" || aot.GameDllDirectory != "Dlls" ||
                host.OutputPackageDirectory != "Package" || host.GameDllDirectory != "Dlls" || package.OutputPackageDirectory != "Package" ||
                string.IsNullOrWhiteSpace(player.PlayerPath) || !player.PlayerPath.StartsWith("Player/", StringComparison.Ordinal) || host.Version != package.Version || player.Version != package.Version)
                throw new InvalidOperationException("归档状态路径或版本不符合固定布局");
            aot.AotDirectory = Resolve(directory, aot.AotDirectory); aot.RuntimeListPath = Resolve(directory, aot.RuntimeListPath); aot.GameDllDirectory = Resolve(directory, aot.GameDllDirectory);
            host.OutputPackageDirectory = Resolve(directory, host.OutputPackageDirectory); host.GameDllDirectory = Resolve(directory, host.GameDllDirectory);
            package.OutputPackageDirectory = Resolve(directory, package.OutputPackageDirectory); player.PlayerPath = Resolve(directory, player.PlayerPath);
            bool valid = platform == "Android" ? BuildStateStore.TryValidateAndroidAotBaseline(state, UnityEditor.BuildTarget.Android, out _) : BuildStateStore.TryValidateAotBaseline(state, UnityEditor.BuildTarget.StandaloneWindows64, out _);
            if (!valid || !File.Exists(player.PlayerPath) || File.ReadAllText(Path.Combine(package.OutputPackageDirectory, "DefaultPackage.version")).Trim() != package.Version)
                throw new InvalidOperationException("基线AOT、Player或资源版本缺失");
            return state;
        }

        private static void CheckIdentity(string platform, string commit, string unity, string boot)
        {
            if ((platform != "PC" && platform != "Android") || !Regex.IsMatch(commit ?? "", @"\A[0-9a-fA-F]{40}\z") || string.IsNullOrWhiteSpace(unity) || string.IsNullOrWhiteSpace(boot))
                throw new InvalidOperationException("宿主归档身份字段缺失");
        }
        private static void RequireNewDestination(string path)
        {
            if (!Path.IsPathRooted(path ?? "")) throw new ArgumentException("基线目录必须是绝对路径");
            WorkspaceBuildLock.EnsureNoLinks(path);
            if (Directory.Exists(path) || File.Exists(path)) throw new IOException("拒绝覆盖已有基线或恢复目录：" + path);
        }
        private static string Resolve(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith("/") || relative.Split('/').Any(part => part == "" || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ")))
                throw new InvalidOperationException("基线相对路径无效");
            string path = Path.GetFullPath(Path.Combine(root, relative)); WorkspaceBuildLock.EnsureNoLinks(path); return path;
        }
        private static string Relative(string root, string file) => file.Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
        private static string Hash(string file) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(file)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static IEnumerable<string> TreeFiles(string root)
        {
            WorkspaceBuildLock.EnsureNoLinks(root);
            foreach (string file in Directory.GetFiles(root)) { WorkspaceBuildLock.EnsureNoLinks(file); yield return file; }
            foreach (string child in Directory.GetDirectories(root)) foreach (string file in TreeFiles(child)) yield return file;
        }
        private static void CopyTree(string source, string destination)
        {
            if (!Directory.Exists(source)) throw new InvalidOperationException("基线来源目录缺失：" + source);
            string src = Path.GetFullPath(source).TrimEnd('\\', '/'), dst = Path.GetFullPath(destination);
            if (dst.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || dst == src) throw new IOException("不能递归归档到来源目录内");
            var files = TreeFiles(src).ToList(); if (files.Count == 0) throw new InvalidOperationException("基线来源目录为空");
            foreach (string file in files) { string target = Resolve(dst, Relative(src, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target); }
        }
    }
}
#endif
