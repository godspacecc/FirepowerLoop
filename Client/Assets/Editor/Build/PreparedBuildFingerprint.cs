using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Shared.UnityTools
{
    /// <summary>对准备产物的路径和内容生成指纹，可以识别增删、重命名和同长度修改。</summary>
    internal static class PreparedBuildFingerprint
    {
        internal static string Capture(string[] paths)
        {
            using var hash = SHA256.Create();
            using var entries = new MemoryStream();
            using var writer = new BinaryWriter(entries, Encoding.UTF8, leaveOpen: true);
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal))
            {
                writer.Write(path);
                using var input = File.OpenRead(path);
                writer.Write(hash.ComputeHash(input));
            }
            writer.Flush();
            entries.Position = 0;
            return Convert.ToBase64String(hash.ComputeHash(entries));
        }
    }
}
