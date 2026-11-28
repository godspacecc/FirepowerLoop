using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SharedBoot
{
    /// <summary>在资源预检和程序集加载之间复用同一份 AOT 清单解析规则。</summary>
    internal static class AotAssemblyManifest
    {
        internal static bool TryParse(string content, string gameAssemblyName, bool allowLegacyEntries,
            out List<string> names, out string invalidName)
        {
            names = new List<string>();
            invalidName = null;
            string gameDll = gameAssemblyName + ".dll";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = line;
                if (allowLegacyEntries)
                {
                    name = line.Trim();
                    if (name.Length == 0 || name.StartsWith("//", StringComparison.Ordinal))
                        continue;
                    if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        name += ".dll";
                }

                if ((!allowLegacyEntries && !Regex.IsMatch(name, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\.dll\z")) ||
                    string.Equals(name, gameDll, StringComparison.OrdinalIgnoreCase) ||
                    !seen.Add(name))
                {
                    invalidName = name;
                    return false;
                }

                names.Add(name);
            }

            return names.Count != 0;
        }
    }
}
