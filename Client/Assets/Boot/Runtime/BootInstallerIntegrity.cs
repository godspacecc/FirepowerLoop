using System;
using System.IO;
using System.Security.Cryptography;

namespace SharedBoot
{
    /// <summary>供 Boot 与独立构建验证使用的安装包大小和 SHA-256 校验。</summary>
    internal static class BootInstallerIntegrity
    {
        internal static string Digest(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        internal static bool Matches(string path, long size, string expectedDigest)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (stream.Length != size)
                    return false;

                using var hash = SHA256.Create();
                string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                return string.Equals(actual, expectedDigest, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
