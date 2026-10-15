#if UNITY_EDITOR
using System.IO;
using System.Xml;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Game.Editor
{
    /// <summary>只读校验热更河谷所需的网格合并 API 保留规则；不生成资源或执行打包。</summary>
    public sealed class MeshCombineHostBuildGuard : IPreprocessBuildWithReport
    {
        /// <summary>独立维护的宿主保留文件，不受 HybridCLR 自动生成清单覆盖。</summary>
        private const string PreservationPath="Assets/Boot/TerrainPreservation/link.xml";
        public int callbackOrder=>-1000;

        /// <summary>仅在用户构建宿主时检查；缺失配置直接拒绝，不自动修补。</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if(!File.Exists(PreservationPath))throw new BuildFailedException("网格合并宿主保留配置缺失："+PreservationPath);
            ValidatePreservation(File.ReadAllText(PreservationPath));
        }

        /// <summary>结构体属性与 Mesh API 都须保留；此源码检查不代表现有PC包已修复。</summary>
        public static void ValidatePreservation(string xml)
        {
            var document=new XmlDocument{XmlResolver=null};
            try{document.LoadXml(xml);}
            catch(XmlException e){throw new BuildFailedException("网格合并宿主保留配置无效："+e.Message);}
            foreach(var type in new[]{"UnityEngine.CombineInstance","UnityEngine.Mesh"})
            {
                if(document.SelectSingleNode("/linker/assembly[@fullname='UnityEngine.CoreModule' and @preserve='all']")!=null)continue;
                if(document.SelectSingleNode($"/linker/assembly[@fullname='UnityEngine.CoreModule']/type[@fullname='{type}' and @preserve='all']")==null)
                    throw new BuildFailedException($"网格合并宿主保留配置缺少 {type}。补齐 {PreservationPath} 后重新构建并安装完整客户端；热更资源不能恢复已裁剪的宿主方法。");
            }
        }
    }
}
#endif
