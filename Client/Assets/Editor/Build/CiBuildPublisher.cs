#if UNITY_EDITOR
using System;
using System.IO;
using Boot;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>显式发布完整的新资源服包；只操作本次清单，失败删除暂存，不启动服务、不覆盖开发发布目录。</summary>
    public static class CiBuildPublisher
    {
        public static void Publish(string marker, string destination)
        {
            var artifact = CiBuildArtifacts.Validate(marker);
            string repo = Directory.GetParent(Application.dataPath).Parent.FullName;
            PublishTransaction(destination, staging =>
            {
                var resource = ResourceServerPublishUtility.PublishExactPackage(artifact.PackageDirectory, artifact.Version,
                    Path.Combine(repo, "Server/ResourceServer"), Path.Combine(staging, "r"));
                if (!resource.Success || resource.Version != artifact.Version) throw new InvalidOperationException("指定资源发布失败：" + resource.Error);
                if (artifact.BuildType == "FirstPackage")
                {
                    if (artifact.Platform == "Android") AndroidApkPublishUtility.Publish(artifact.PlayerPath, resource.BundleDirectory, artifact.AppVersion, artifact.AndroidVersionCode);
                    else WindowsInstallerPublishUtility.PublishGenerated(artifact.InstallerDirectory, resource.BundleDirectory);
                }
                // 整包仍在未公开暂存目录内，再核对原产物未被并发更改。
                CiBuildArtifacts.Validate(marker);
                foreach (string directory in Directory.GetDirectories(resource.BundleDirectory)) Directory.Move(directory, Path.Combine(staging, Path.GetFileName(directory)));
                foreach (string file in Directory.GetFiles(resource.BundleDirectory)) File.Move(file, Path.Combine(staging, Path.GetFileName(file)));
                Directory.Delete(Path.Combine(staging, "r"), true);
            });
        }

        /// <summary>目标必须全新，所有准备成功后同卷改名；可注入文件动作验收失败清理。</summary>
        public static void PublishTransaction(string destination, Action<string> prepare)
        {
            if (!Path.IsPathRooted(destination ?? "")) throw new ArgumentException("CI发布目标必须是绝对路径");
            WorkspaceBuildLock.EnsureNoLinks(destination);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("CI发布目标已存在，拒绝覆盖");
            // 使用短同级目录，避免Windows旧文件API在嵌套资源事务中越过MAX_PATH。
            string staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination)), ".ci-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(staging); prepare(staging);
                WorkspaceBuildLock.EnsureNoLinks(destination); Directory.Move(staging, destination);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
    }
}
#endif
