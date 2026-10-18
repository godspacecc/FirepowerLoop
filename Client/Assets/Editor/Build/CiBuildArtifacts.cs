#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace Game.Editor
{
    /// <summary>本次产物的固定身份和文件清单；发布前重新检查，绝不选取另一轮的最新包。</summary>
    [Serializable]
    public sealed class CiBuildArtifacts
    {
        [Serializable] public sealed class Item { public string Path, Sha256; public long Size; }
        public int SchemaVersion;
        public string ProjectId, Platform, BuildType, Version, PackageDirectory, PlayerPath, InstallerDirectory, BaselineDirectory, AppVersion;
        public int AndroidVersionCode;
        public List<Item> Files;

        public static string Seal(string marker, string json)
        {
            if (File.Exists(marker)) throw new IOException("产物清单已存在，拒绝覆盖");
            WorkspaceBuildLock.EnsureNoLinks(marker);
            var value = JsonConvert.DeserializeObject<CiBuildArtifacts>(json) ?? throw new InvalidOperationException("产物为空");
            value.CheckIdentity();
            value.Files = value.GetFiles().Select(path => new Item { Path = path, Size = new FileInfo(path).Length, Sha256 = Hash(path) }).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(marker));
            File.WriteAllText(marker, JsonConvert.SerializeObject(value, Formatting.Indented));
            Validate(marker); return marker;
        }

        public static CiBuildArtifacts Validate(string marker)
        {
            WorkspaceBuildLock.EnsureNoLinks(marker);
            var value = JsonConvert.DeserializeObject<CiBuildArtifacts>(File.ReadAllText(marker)) ?? throw new InvalidOperationException("产物清单为空");
            value.CheckIdentity();
            var actual = value.GetFiles().ToList();
            if (value.Files == null || value.Files.Count != actual.Count || value.Files.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != actual.Count)
                throw new InvalidOperationException("产物文件增减或清单重复");
            var paths = new HashSet<string>(actual, StringComparer.OrdinalIgnoreCase);
            foreach (var item in value.Files)
                if (!paths.Contains(item.Path) || new FileInfo(item.Path).Length != item.Size || Hash(item.Path) != item.Sha256) throw new InvalidOperationException("产物文件长度或哈希不符：" + item.Path);
            return value;
        }

        private void CheckIdentity()
        {
            if (SchemaVersion != 1 || ProjectId != "FirepowerLoop" || (Platform != "PC" && Platform != "Android") || (BuildType != "FirstPackage" && BuildType != "HostUpdate"))
                throw new InvalidOperationException("产物项目、平台或类型无效");
            if (!Path.IsPathRooted(PackageDirectory ?? "") || !Directory.Exists(PackageDirectory) || string.IsNullOrWhiteSpace(Version) || File.ReadAllText(Path.Combine(PackageDirectory, "DefaultPackage.version")).Trim() != Version)
                throw new InvalidOperationException("产物资源版本不符");
            if (BuildType == "FirstPackage")
            {
                if (!Path.IsPathRooted(PlayerPath ?? "") || !File.Exists(PlayerPath) || new FileInfo(PlayerPath).Length == 0 || !Directory.Exists(BaselineDirectory)) throw new InvalidOperationException("首包Player或独立基线缺失");
                if (Platform == "PC" && !Directory.Exists(InstallerDirectory)) throw new InvalidOperationException("PC首包安装器缺失");
                if (Platform == "Android" && (string.IsNullOrWhiteSpace(AppVersion) || AndroidVersionCode <= 0)) throw new InvalidOperationException("APK版本身份缺失");
            }
        }
        private IEnumerable<string> GetFiles()
        {
            var files = Tree(PackageDirectory).ToList();
            if (BuildType == "FirstPackage")
            {
                if (Platform == "PC") { files.AddRange(Tree(Path.GetDirectoryName(PlayerPath))); files.AddRange(Tree(InstallerDirectory)); }
                else { WorkspaceBuildLock.EnsureNoLinks(PlayerPath); files.Add(Path.GetFullPath(PlayerPath)); }
                files.AddRange(Tree(BaselineDirectory));
            }
            return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal);
        }
        private static IEnumerable<string> Tree(string directory)
        {
            WorkspaceBuildLock.EnsureNoLinks(directory);
            foreach (string file in Directory.GetFiles(directory)) { WorkspaceBuildLock.EnsureNoLinks(file); yield return Path.GetFullPath(file); }
            foreach (string child in Directory.GetDirectories(directory)) foreach (string file in Tree(child)) yield return file;
        }
        private static string Hash(string path) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif
