using System;
using System.IO;
using System.Text;

namespace SharedBoot
{
    /// <summary>按启动诊断约定限制单行文本，并原子替换最近一次报告。</summary>
    internal static class BootDiagnosticArtifact
    {
        internal static string BoundedLine(string text, int length)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            int lineBreak = text.IndexOfAny(new[] { '\r', '\n' });
            int count = lineBreak < 0 ? text.Length : lineBreak;
            return text.Substring(0, Math.Min(count, length));
        }

        internal static void WriteLatest(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                try
                {
                    File.Move(temporary, path);
                }
                catch (IOException) when (File.Exists(path))
                {
                    File.Replace(temporary, path, null, true);
                }
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
