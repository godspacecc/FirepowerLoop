using System.Globalization;

namespace Game
{
    /// <summary>仅格式化下载字节数供 UI 展示；不判断缓存就绪或决定是否下载。</summary>
    public static class ResourceDownloadSize
    {
        /// <summary>按实际大小选择 B、KB 或 MB，非零小文件不会因 MB 舍入显示成零。</summary>
        /// <param name="bytes">字节数；零或负值显示为 0 B。</param>
        public static string Format(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("F1", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024d * 1024d)).ToString("F1", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
