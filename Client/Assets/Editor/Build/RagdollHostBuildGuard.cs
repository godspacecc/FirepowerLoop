#if UNITY_EDITOR
using System.IO;
using System.Xml;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Game.Editor
{
    /// <summary>检查热更敌人布娃娃依赖的宿主物理保留规则；不启动构建或改写生成清单。</summary>
    public sealed class RagdollHostBuildGuard : IPreprocessBuildWithReport
    {
        /// <summary>独立维护的宿主保留文件，避免 HybridCLR 重新生成时覆盖。</summary>
        private const string PreservationPath = "Assets/Boot/TerrainPreservation/link.xml";
        public int callbackOrder => -1000;

        /// <summary>只在用户构建宿主时校验；规则不完整即拒绝，不静默修改设置。</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!File.Exists(PreservationPath))
                throw new BuildFailedException("布娃娃宿主保留配置缺失：" + PreservationPath);
            ValidatePreservation(File.ReadAllText(PreservationPath));
        }

        /// <summary>完整保留物理模块，覆盖 CharacterJoint 原生组件及热更新调用的关节、刚体 API。</summary>
        public static void ValidatePreservation(string xml)
        {
            var document = new XmlDocument { XmlResolver = null };
            try { document.LoadXml(xml); }
            catch (XmlException error)
            {
                throw new BuildFailedException("布娃娃宿主保留配置无效：" + error.Message);
            }
            if (document.SelectSingleNode(
                "/linker/assembly[@fullname='UnityEngine.PhysicsModule' and @preserve='all']") == null)
                throw new BuildFailedException(
                    $"布娃娃宿主保留配置缺少完整 UnityEngine.PhysicsModule（含 CharacterJoint）。补齐 {PreservationPath} 后重新构建并安装完整客户端；热更资源不能恢复已裁剪的原生组件。");
        }
    }
}
#endif
