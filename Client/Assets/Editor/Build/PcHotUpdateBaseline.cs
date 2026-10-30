#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using dnlib.DotNet;

namespace Game.Editor
{
    /// <summary>把热更新限制在已成功生成的PC宿主上，基线不随热更新覆盖。</summary>
    internal static class PcHotUpdateBaseline
    {
        // 无法确认兼容时要求重新生成宿主，不通过重记当前输入绕过检查。
        private const string RebuildHint = "请执行 PC 完整打包或启动包，并让测试端使用新游戏后再构建热更新。";
        // 固定文件同时绑定发布身份、原生游戏代码和Unity运行库。
        private static readonly string[] PlayerFiles = { "FirepowerLoop.exe", "UnityPlayer.dll", "GameAssembly.dll", "FirepowerLoop_Data/StreamingAssets/windows-release.json" };
        private const string ListName = "HybridCLRAssemblyList.bytes";

        [Serializable] private sealed class Baseline
        {
            public int Schema = 1; // 基线格式，旧记录不能冒认为新格式。
            public string Inputs; // 宿主输入指纹。
            public List<Entry> Aot = new List<Entry>(); // 归档的补充元数据文件。
            public List<Entry> Player = new List<Entry>(); // 实际输出程序的身份指纹。
            public string ListHash; // 运行清单内容指纹。
            public string GameHash; // 本次宿主配套Game DLL内容，用于原生调用的保守兼容门禁。
        }
        [Serializable] private sealed class Entry
        {
            public string Name; // 固定目录内相对文件名。
            public string Hash; // 文件SHA256。
        }

        /// <summary>仅完整包或启动包的Player通过验证后调用；从本次打入资源的DLL复制AOT。</summary>
        internal static void Capture(string directory, string player, string dllDirectory, string inputs)
        {
            if (string.IsNullOrWhiteSpace(inputs) || Directory.Exists(directory) || File.Exists(directory))
                throw new InvalidOperationException("PC宿主基线缺少输入或目标已存在，禁止覆盖。" + RebuildHint);
            var record = new Baseline { Inputs = inputs };
            string list = Path.Combine(dllDirectory, ListName);
            string[] names = ReadNames(list);
            foreach (string name in names)
                record.Aot.Add(new Entry { Name = name, Hash = Hash(Path.Combine(dllDirectory, name + ".bytes")) });
            foreach (string name in PlayerFiles)
                record.Player.Add(new Entry { Name = name, Hash = Hash(Path.Combine(Path.GetDirectoryName(player), name)) });
            record.ListHash = Hash(list);
            record.GameHash = Hash(Path.Combine(dllDirectory, "Game.dll.bytes"));
            // 新批次只写独立目录；中途失败保留.pending供排查，不改变已有基线。
            string pending = directory + ".pending-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(pending, "Aot"));
            foreach (string name in names) File.Copy(Path.Combine(dllDirectory, name + ".bytes"), Path.Combine(pending, "Aot", name));
            File.Copy(list, Path.Combine(pending, ListName));
            File.Copy(Path.Combine(dllDirectory, "Game.dll.bytes"), Path.Combine(pending, "Game.dll"));
            File.WriteAllText(Path.Combine(pending, "baseline.json"), JsonUtility.ToJson(record, true), new UTF8Encoding(false));
            Validate(pending, player, inputs);
            Directory.Move(pending, directory);
        }

        /// <summary>核对真实Player、独立AOT及当前输入，缺失旧基线时拒绝猜测兼容性。</summary>
        internal static void Validate(string directory, string player, string inputs)
        {
            try
            {
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(player)) throw new InvalidOperationException("缺少PC宿主基线或Player记录。");
                var record = JsonUtility.FromJson<Baseline>(File.ReadAllText(Path.Combine(directory, "baseline.json")));
                if (record == null || record.Schema != 1 || string.IsNullOrEmpty(record.Inputs) || record.Inputs != inputs)
                    throw new InvalidOperationException("PC宿主代码、插件或构建输入已变化，不能只更新资源。");
                if (Hash(Path.Combine(directory, "Game.dll")) != record.GameHash)
                    throw new InvalidOperationException("PC基线Game参考程序集已变化。");
                if (Path.GetFileName(player) != "FirepowerLoop.exe" || record.Player == null || record.Player.Count != PlayerFiles.Length)
                    throw new InvalidOperationException("PC宿主身份不完整。");
                foreach (string name in PlayerFiles)
                {
                    var entries = record.Player.Where(entry => entry != null && entry.Name == name).ToArray();
                    if (entries.Length != 1 || entries[0].Hash != Hash(Path.Combine(Path.GetDirectoryName(player), name)))
                        throw new InvalidOperationException("PC宿主文件不匹配：" + name);
                }
                string list = Path.Combine(directory, ListName);
                if (Hash(list) != record.ListHash) throw new InvalidOperationException("PC基线运行清单已变化。");
                string[] names = ReadNames(list);
                if (record.Aot == null || record.Aot.Count != names.Length) throw new InvalidOperationException("PC基线AOT记录不完整。");
                foreach (string name in names)
                {
                    var entries = record.Aot.Where(entry => entry != null && entry.Name == name).ToArray();
                    if (entries.Length != 1 || entries[0].Hash != Hash(Path.Combine(directory, "Aot", name)))
                        throw new InvalidOperationException("PC基线AOT不匹配：" + name);
                }
            }
            catch (Exception error) { throw new InvalidOperationException(error.Message + "\n" + RebuildHint, error); }
        }

        /// <summary>原生调用/回调需要宿主桥接；含这类需求的Game发生变化时保守要求重出程序。</summary>
        internal static void ValidateHotUpdateAssembly(string directory, string gameDll)
        {
            var record = JsonUtility.FromJson<Baseline>(File.ReadAllText(Path.Combine(directory, "baseline.json")));
            if (string.IsNullOrEmpty(record?.GameHash)) throw new InvalidOperationException("缺少Game桥接兼容记录。" + RebuildHint);
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(gameDll)))
            {
                bool requiresNativeBridge = module.GetTypes().SelectMany(type => type.Methods).Any(method =>
                    method.IsPinvokeImpl || method.CustomAttributes.Any(attribute =>
                        attribute.AttributeType.Name == "MonoPInvokeCallbackAttribute" || attribute.AttributeType.Name == "UnmanagedCallersOnlyAttribute") ||
                    (method.HasBody && method.Body.Instructions.Any(instruction => instruction.OpCode.Code == dnlib.DotNet.Emit.Code.Calli)));
                // 不尝试猜测旧原生桥接是否足够；未变更的原始DLL可用，新增/修改一律要求新宿主。
                if (requiresNativeBridge && Hash(gameDll) != record.GameHash)
                    throw new InvalidOperationException("Game包含原生调用或回调且DLL已变化，无法确认旧宿主桥接兼容。" + RebuildHint);
            }
            ValidateManagedReferences(Path.Combine(directory, "Game.dll"), gameDll);
        }

        /// <summary>新外部类型/成员可能已被旧宿主裁掉；只允许初始Game已使用的需求集合。</summary>
        internal static void ValidateManagedReferences(string baselineGame, string currentGame)
        {
            var supported = ExternalRequirements(baselineGame);
            string added = ExternalRequirements(currentGame).FirstOrDefault(requirement => !supported.Contains(requirement));
            if (added != null)
                throw new InvalidOperationException("热更Game新增外部类型或成员需求，无法确认旧宿主已保留：" + added + "\n" + RebuildHint);
        }

        /// <summary>从真实DLL读取外部引用与具体泛型调用，不执行Game或生成新的宿主裁剪文件。</summary>
        private static HashSet<string> ExternalRequirements(string dll)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(dll)))
            {
                string ownAssembly = module.Assembly?.FullName;
                Action<ITypeDefOrRef, string> add = (type, requirement) =>
                {
                    string assembly = type?.DefinitionAssembly?.FullName;
                    if (assembly != null && assembly != ownAssembly) result.Add(assembly + "|" + requirement);
                };
                foreach (var type in module.GetTypeRefs()) add(type, type.FullName);
                foreach (var member in module.GetMemberRefs()) add(member.DeclaringType, member.FullName);
                foreach (var method in module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is IMethod called) add(called.DeclaringType, called.FullName);
                        else if (instruction.Operand is IField field) add(field.DeclaringType, field.FullName);
                        else if (instruction.Operand is ITypeDefOrRef type) add(type, type.FullName);
                    }
            }
            return result;
        }

        /// <summary>收集非热更运行代码和原生依赖，同时覆盖程序集布局、Boot资源及构建设置。</summary>
        internal static string CaptureCurrentInputs()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("必须在Windows64目标重载后核对PC基线。" + RebuildHint);
            string root = Directory.GetParent(Application.dataPath).FullName;
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Player))
            {
                if (assembly.name == "Game") continue;
                foreach (string source in assembly.sourceFiles)
                    if (!Path.GetFullPath(source).Replace('\\', '/').Contains("/Assets/HybridCLRGenerate/")) files.Add(Path.GetFullPath(source));
                foreach (string reference in assembly.compiledAssemblyReferences)
                    if (!Path.GetFullPath(reference).Replace('\\', '/').Contains("/Library/ScriptAssemblies/")) files.Add(Path.GetFullPath(reference));
            }
            foreach (string dependency in AssetDatabase.GetDependencies("Assets/Game/Scenes/Boot.unity", true))
                if (File.Exists(dependency)) files.Add(Path.GetFullPath(dependency));
            return ComputeInputFingerprint(root, files.ToArray(), Application.unityVersion);
        }

        /// <summary>冻结进入本轮资源构建的代码bytes，防止Player构建期间源目录变化后归档错AOT。</summary>
        internal static string CaptureRuntimeDllInputs()
        {
            string directory = HybridCLRBuildUtility.GetGameDllDirectory();
            string[] names = ReadNames(Path.Combine(directory, ListName));
            return string.Join("|", names.Select(name => name + ":" + Hash(Path.Combine(directory, name + ".bytes")))) +
                "|" + Hash(Path.Combine(directory, ListName)) + "|" + Hash(Path.Combine(directory, "Game.dll.bytes"));
        }

        /// <summary>按相对路径及内容指纹识别新增/删除/同长度修改，Game普通逻辑与远程资源允许变化。</summary>
        internal static string ComputeInputFingerprint(string root, string[] additionalFiles, string unityVersion)
        {
            root = Path.GetFullPath(root);
            string assets = Path.Combine(root, "Assets");
            var files = new HashSet<string>(additionalFiles.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            foreach (string folder in new[] { "ProjectSettings", "Packages" })
                foreach (string file in Directory.GetFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories)) files.Add(file);
            foreach (string file in Directory.GetFiles(assets, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(assets, file).Replace('\\', '/');
                string ext = Path.GetExtension(file).ToLowerInvariant();
                bool editor = relative.Split('/').Any(part => part == "Editor" || part == "Tests");
                bool generated = relative.StartsWith("HybridCLRGenerate/", StringComparison.Ordinal);
                bool gameSource = ext == ".cs" && relative.StartsWith("Game/", StringComparison.Ordinal) && IsGameAssemblySource(file, assets);
                bool embedded = relative.StartsWith("Boot/", StringComparison.Ordinal) || relative.Contains("/Resources/") || relative.StartsWith("Resources/", StringComparison.Ordinal) || relative.Contains("/Plugins/") || relative.StartsWith("Plugins/", StringComparison.Ordinal);
                bool streaming = relative.StartsWith("StreamingAssets/", StringComparison.Ordinal) && !relative.StartsWith("StreamingAssets/yoo/", StringComparison.Ordinal) && relative != "StreamingAssets/windows-release.json" && relative != "StreamingAssets/windows-release.json.meta";
                if (!editor && !generated && (embedded || streaming || relative == "Game/Scenes/Boot.unity" ||
                    (ext == ".cs" && !gameSource) || ext == ".asmdef" || ext == ".asmref" || ext == ".rsp" || ext == ".dll" || Path.GetFileName(file) == "link.xml")) files.Add(file);
            }
            foreach (string file in files.ToArray()) if (File.Exists(file + ".meta")) files.Add(file + ".meta");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            using (var sha = SHA256.Create())
            {
                writer.Write("pc-host-v1:" + unityVersion);
                // Unity安装目录等工程外引用按逻辑文件名和内容识别，不绑定机器盘符或checkout位置。
                var inputs = files.Select(file => new { Name = InputIdentity(root, file), Hash = Hash(file, true) })
                    .Select(file => file.Name + "|" + file.Hash).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
                foreach (string input in inputs)
                {
                    writer.Write(input);
                }
                writer.Flush(); stream.Position = 0;
                return Convert.ToBase64String(sha.ComputeHash(stream));
            }
        }

        /// <summary>工程内保留布局，工程外只取逻辑身份，内容指纹另行参与计算。</summary>
        private static string InputIdentity(string root, string file)
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            return relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? "external/" + Path.GetFileName(file) : relative;
        }

        /// <summary>只放行根Game程序集里的源码；嵌套程序集或asmref保守视为宿主输入。</summary>
        private static bool IsGameAssemblySource(string file, string assets)
        {
            string directory = Path.GetDirectoryName(file);
            string game = Path.Combine(assets, "Game");
            while (directory != null && directory.Length >= game.Length)
            {
                if (Directory.GetFiles(directory, "*.asmref").Length > 0) return false;
                string[] definitions = Directory.GetFiles(directory, "*.asmdef");
                if (definitions.Length > 0) return directory == game && definitions.Length == 1 && Path.GetFileName(definitions[0]) == "Game.asmdef";
                directory = Path.GetDirectoryName(directory);
            }
            return false;
        }

        /// <summary>仅接受非空、无重复的普通DLL文件名，拒绝越界路径。</summary>
        private static string[] ReadNames(string path)
        {
            string[] names = File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
            if (names.Length == 0 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length ||
                names.Any(name => name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.Equals("Game.dll", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("PC基线AOT清单无效。");
            return names;
        }

        /// <summary>文件必须存在且非空，避免把空产物记录为成功基线。</summary>
        private static string Hash(string path, bool allowEmpty = false)
        {
            if (!File.Exists(path) || (!allowEmpty && new FileInfo(path).Length == 0)) throw new FileNotFoundException("PC基线输入缺失或为空：" + path);
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(path)) return Convert.ToBase64String(sha.ComputeHash(file));
        }
    }
}
#endif
