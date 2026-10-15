#if UNITY_EDITOR
using System.IO;
using System.Xml;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Game.Editor
{
    /// <summary>
    /// 校验宿主对地图导航数据及热更导航 API 的完整保留，不执行打包或修改生成清单。
    /// 源码校验不代表 APK 已包含所需类型，最终仍须安装新宿主并进行真机验收。
    /// </summary>
    public sealed class NavigationHostBuildGuard : IPreprocessBuildWithReport
    {
        /// <summary>手工维护的独立保留文件；不依赖 HybridCLR 自动生成的 link.xml。</summary>
        private const string PreservationPath = "Assets/Boot/TerrainPreservation/link.xml";
        public int callbackOrder => -1000;

        /// <summary>仅在用户执行宿主构建时调用；缺失规则立即阻止构建，不自动补写。</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!File.Exists(PreservationPath))
                throw new BuildFailedException($"导航宿主保留配置缺失：{PreservationPath}");
            ValidatePreservation(File.ReadAllText(PreservationPath));
        }

        /// <summary>只读验证完整导航模块保留；仅保留 NavMeshData 无法覆盖热更新增方法。</summary>
        /// <param name="xml">独立 link.xml 内容；解析失败或规则不完整均拒绝。</param>
        public static void ValidatePreservation(string xml)
        {
            var document = new XmlDocument { XmlResolver = null };
            try
            {
                document.LoadXml(xml);
            }
            catch (XmlException exception)
            {
                throw new BuildFailedException($"导航宿主保留配置无效：{exception.Message}");
            }

            if (document.SelectSingleNode("/linker/assembly[@fullname='UnityEngine.AIModule' and @preserve='all']") == null)
                throw new BuildFailedException($"导航宿主保留配置缺少完整 UnityEngine.AIModule。补齐 {PreservationPath} 后重新构建并安装 APK；热更不能恢复已裁剪的宿主类型与方法。");
        }
    }
}
#endif
