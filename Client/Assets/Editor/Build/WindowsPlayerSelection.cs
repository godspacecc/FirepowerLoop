using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Shared.UnityTools
{
    /// <summary>验证手动选择的 PC 游戏与同目录构建产物，阻止将安装器或另一工程的游戏当作输入。</summary>
    internal static class WindowsPlayerSelection
    {
        internal const string Menu = "Tools/打包/PC/生成安装器";

        [Serializable]
        private sealed class Identity
        {
            public int schema, schemaVersion, releaseNumber;
            public string productId, platform, architecture, versionName;
        }

        internal static string RequirePlayerDirectory(string executable, string playerName, string productId, int schema)
        {
            string path = Path.GetFullPath(executable);
            if (!string.Equals(Path.GetFileName(path), playerName + ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择 " + playerName + ".exe。");
            string player = Path.GetDirectoryName(path);
            string data = Path.Combine(player, playerName + "_Data");
            string identityPath = Path.Combine(data, "StreamingAssets", "windows-release.json");
            foreach (string file in new[] { path, Path.Combine(player, "UnityPlayer.dll"),
                Path.Combine(player, "GameAssembly.dll"), Path.Combine(data, "ScriptingAssemblies.json"), identityPath })
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length == 0)
                    throw new FileNotFoundException("PC 游戏缺少必需文件：" + file, file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("PC 游戏文件不能是链接：" + file);
            }

            if (new FileInfo(identityPath).Length > 4096)
                throw new InvalidDataException("PC 游戏发行信息文件过大。");
            var identity = JsonUtility.FromJson<Identity>(File.ReadAllText(identityPath));
            if (identity == null || (schema == 2 ? identity.schema : identity.schemaVersion) != schema ||
                identity.productId != productId || identity.platform != "Windows" || identity.architecture != "x64" ||
                identity.releaseNumber < 1 || string.IsNullOrWhiteSpace(identity.versionName))
                throw new InvalidDataException("PC 游戏的产品、平台或发行版本不匹配。");
            RejectLinks(player);
            return player;
        }

        private static void RejectLinks(string player)
        {
            var directories = new Stack<string>();
            directories.Push(player);
            while (directories.Count > 0)
            {
                string directory = directories.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("游戏目录不能包含链接：" + directory);
                foreach (string item in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(item);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("游戏目录不能包含链接：" + item);
                    if ((attributes & FileAttributes.Directory) != 0)
                        directories.Push(item);
                }
            }
        }

    }
}
