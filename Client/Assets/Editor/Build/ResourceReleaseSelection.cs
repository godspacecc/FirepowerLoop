using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Shared.UnityTools
{
    /// <summary>按平台和实际安装包记录资源产物，供更新菜单选择当前宿主可用的最新版本。</summary>
    internal static class ResourceReleaseSelection
    {
        [Serializable]
        internal sealed class Entry
        {
            public string target, host, version, package;
        }

        private static string Root(string target) => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../..", "Backup", "Artifacts", "UnityTools", "ResourceReleases", target));

        internal static void Record(string target, string host, string version, string package)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(version) || !Directory.Exists(package))
                return;

            string root = Root(target);
            Directory.CreateDirectory(root);
            string name = Guid.NewGuid().ToString("N");
            string temporary = Path.Combine(root, name + ".tmp");
            try
            {
                var entry = new Entry { target = target, host = host, version = version, package = Path.GetFullPath(package) };
                File.WriteAllText(temporary, JsonUtility.ToJson(entry));
                File.Move(temporary, Path.Combine(root, name + ".json"));
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static IEnumerable<Entry> ForHost(string target, string host)
        {
            string root = Root(target);
            if (string.IsNullOrWhiteSpace(host) || !Directory.Exists(root))
                yield break;
            foreach (string file in Directory.EnumerateFiles(root, "*.json"))
            {
                Entry entry;
                try { entry = JsonUtility.FromJson<Entry>(File.ReadAllText(file)); }
                catch (Exception error) when (error is IOException || error is ArgumentException) { continue; }
                if (entry?.target == target && entry.host == host && !string.IsNullOrWhiteSpace(entry.version) &&
                    !string.IsNullOrWhiteSpace(entry.package))
                    yield return entry;
            }
        }

        internal static T LatestValid<T>(IEnumerable<T> entries, Func<T, string> version, Func<T, bool> valid) where T : class =>
            entries.OrderByDescending(version, StringComparer.Ordinal).FirstOrDefault(valid);
    }
}
