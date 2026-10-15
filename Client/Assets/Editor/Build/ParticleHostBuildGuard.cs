#if UNITY_EDITOR
using System.IO;
using System.Xml;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Game.Editor
{
    /// <summary>只读检查热更天气依赖的宿主粒子 API 保留规则；不生成资源、不启动打包、不修改天气状态。</summary>
    public sealed class ParticleHostBuildGuard : IPreprocessBuildWithReport
    {
        /// <summary>随项目维护的宿主保留配置，不由 HybridCLR 自动生成文件替代。</summary>
        private const string PreservationPath = "Assets/Boot/TerrainPreservation/link.xml";

        public int callbackOrder => -1000;

        /// <summary>用户构建宿主时执行；配置缺失或不完整即拒绝，不自动修补。</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!File.Exists(PreservationPath))
                throw new BuildFailedException("粒子宿主保留配置缺失：" + PreservationPath);
            ValidatePreservation(File.ReadAllText(PreservationPath));
        }

        /// <summary>要求完整保留粒子模块，覆盖 EmitParams 和模块结构体属性；通过不代表旧客户端已经修复。</summary>
        /// <param name="xml">独立保留文件内容；无效 XML 或不完整规则均拒绝。</param>
        public static void ValidatePreservation(string xml)
        {
            var document = new XmlDocument { XmlResolver = null };
            try { document.LoadXml(xml); }
            catch (XmlException error)
            {
                throw new BuildFailedException("粒子宿主保留配置无效：" + error.Message);
            }

            // 只保留 ParticleSystem 外层类型不足以保证嵌套结构体属性及各模块 API 可用。
            if (document.SelectSingleNode(
                "/linker/assembly[@fullname='UnityEngine.ParticleSystemModule' and @preserve='all']") == null)
                throw new BuildFailedException(
                    $"粒子宿主保留配置缺少完整 UnityEngine.ParticleSystemModule。补齐 {PreservationPath} 后重新构建并安装完整客户端；热更资源不能恢复已裁剪的 EmitParams 等宿主方法。");
        }
    }
}
#endif
