#if UNITY_EDITOR
using System.IO;

namespace Game.Editor
{
    internal static class ProjectBuildPaths
    {
        private const string DirectoryName = "ProjectBuild";
        private const string LegacyDirectoryName = "FirepowerLoopBuild";

        internal static string LibraryRoot(string projectRoot) =>
            Path.Combine(projectRoot, "Library", DirectoryName);

        internal static string LegacyLibraryRoot(string projectRoot) =>
            Path.Combine(projectRoot, "Library", LegacyDirectoryName);

        internal static string TemporaryPath(string projectRoot, params string[] parts) =>
            Path.Combine(Path.Combine(projectRoot, "Temp", DirectoryName), Path.Combine(parts));

        // Existing mutable records stay together with their backups or lock files.
        // A fresh workspace writes directly to ProjectBuild.
        internal static string ExistingRecord(string projectRoot, string fileName, string lockSuffix = null)
        {
            string legacy = Path.Combine(LegacyLibraryRoot(projectRoot), fileName);
            bool hasLegacyLock = lockSuffix != null && File.Exists(legacy + lockSuffix);
            return File.Exists(legacy) || hasLegacyLock ? legacy : Path.Combine(LibraryRoot(projectRoot), fileName);
        }
    }
}
#endif
