using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Shared.UnityTools
{
    /// <summary>统一命令行选包和便携索引。工程适配负责旧格式与实际 AOT 兼容校验。</summary>
    public static class HostBaselineCommand
    {
        [Serializable]
        private sealed class Reference
        {
            public string format, project, target, nativeRecord, sha256;
        }

        private const string Format = "unity-tools-host-baseline-v1";

        public static void BuildFromCommandLine()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            var target = EditorUserBuildSettings.activeBuildTarget;
            if (target != BuildTarget.Android && target != BuildTarget.StandaloneWindows64)
                throw new BuildFailedException("热更新脚本要求 Android 或 Windows x64 构建目标。");
            string file = ReadArgument(arguments);
            string record = Resolve(file, HostBaselineProject.ProjectId, target);
            HostBaselineProject.Build(record, target, !arguments.Contains("-buildOnly", StringComparer.Ordinal));
        }

        internal static string ReadArgument(string[] arguments)
        {
            int option = Array.IndexOf(arguments, "-hostBaseline");
            if (option < 0 || option != Array.LastIndexOf(arguments, "-hostBaseline") ||
                option + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[option + 1]) ||
                arguments[option + 1].StartsWith("-", StringComparison.Ordinal))
                throw new BuildFailedException("请传入一个 -hostBaseline <安装包记录文件>。");
            string path = Path.GetFullPath(arguments[option + 1]);
            if (!File.Exists(path))
                throw new BuildFailedException("找不到安装包记录文件：" + path);
            return path;
        }

        internal static string Resolve(string file, string project, BuildTarget target)
        {
            Reference reference;
            try { reference = JsonUtility.FromJson<Reference>(File.ReadAllText(file)); }
            catch (ArgumentException) { throw new BuildFailedException("安装包记录 JSON 无效：" + file); }
            if (reference == null)
                throw new BuildFailedException("安装包记录为空：" + file);
            if (string.IsNullOrEmpty(reference.format))
                return file;
            if (reference.format != Format || reference.project != project || reference.target != target.ToString())
                throw new BuildFailedException("安装包记录的版本、工程或平台不匹配：" + file);
            string root = Path.GetDirectoryName(Path.GetFullPath(file));
            string relative = reference.nativeRecord;
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                relative.Split('/', '\\').Any(part => part == ".." || part == "." || part == "" || part.Contains(":")))
                throw new BuildFailedException("安装包记录引用了无效归档路径。");
            string native = Path.GetFullPath(Path.Combine(root, relative));
            if (!File.Exists(native) || !string.Equals(Hash(native), reference.sha256, StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("安装包归档缺失或记录已被修改：" + native);
            return native;
        }

        internal static string WriteReference(string native, string project, BuildTarget target)
        {
            native = Path.GetFullPath(native);
            string directory = Path.GetDirectoryName(native);
            string root = Path.GetDirectoryName(directory);
            string path = Path.Combine(root, Path.GetFileName(directory) + ".unity-host-baseline.json");
            var reference = new Reference
            {
                format = Format, project = project, target = target.ToString(),
                nativeRecord = Path.GetRelativePath(root, native).Replace('\\', '/'), sha256 = Hash(native)
            };
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(reference, true));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }

        private static string Hash(string path)
        {
            using var hash = SHA256.Create();
            using var input = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
        }
    }
}
