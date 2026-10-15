#if UNITY_EDITOR
using System;
using System.IO;
using System.Xml;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Game.Editor
{
    /// <summary>
    /// 在宿主构建前校验地图地形的独立保留规则，防止只保留渲染而裁掉碰撞。
    /// 不执行构建、不修改生成清单，也不代替真机地形与 Shader 验收。
    /// </summary>
    public sealed class TerrainHostBuildGuard : IPreprocessBuildWithReport
    {
        private const string PreservationPath = "Assets/Boot/TerrainPreservation/link.xml";
        public int callbackOrder => -1000;

        /// <summary>用户执行 Player 构建时调用；规则缺失立即阻止构建，不尝试自动补写。</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!File.Exists(PreservationPath))
                throw new BuildFailedException($"地形宿主保留配置缺失：{PreservationPath}");
            ValidatePreservation(File.ReadAllText(PreservationPath));
        }

        /// <summary>只读校验地形与碰撞的裁剪边界；允许按整个模块或具体类型保留。</summary>
        /// <param name="xml">手工维护的 link.xml 内容，不依赖 HybridCLR 的生成文件。</param>
        public static void ValidatePreservation(string xml)
        {
            var document = new XmlDocument { XmlResolver = null };
            try
            {
                document.LoadXml(xml);
            }
            catch (XmlException exception)
            {
                throw new BuildFailedException($"地形宿主保留配置无效：{exception.Message}");
            }

            RequireType(document, "UnityEngine.TerrainModule", "UnityEngine.Terrain");
            RequireType(document, "UnityEngine.TerrainModule", "UnityEngine.TerrainData");
            RequireType(document, "UnityEngine.TerrainModule", "UnityEngine.TerrainLayer");
            RequireType(document, "UnityEngine.TerrainPhysicsModule", "UnityEngine.TerrainCollider");
        }

        private static void RequireType(XmlDocument document, string assembly, string type)
        {
            // 类型所在程序集必须完整保留或显式保留该类型；只保留 Terrain 不会保留 TerrainCollider。
            var retained = document.SelectSingleNode(
                $"/linker/assembly[@fullname='{assembly}' and @preserve='all']") ??
                document.SelectSingleNode(
                    $"/linker/assembly[@fullname='{assembly}']/type[@fullname='{type}' and @preserve='all']");
            if (retained == null)
                throw new BuildFailedException($"地形宿主保留配置缺少 {type}（{assembly}）。补齐 {PreservationPath} 后重新构建 APK。");
        }
    }
}
#endif
