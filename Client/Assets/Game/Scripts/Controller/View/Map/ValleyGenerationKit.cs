using System.Linq;
using UnityEngine;

namespace Game
{
    /// <summary>保存已正式接入的生成资源与交互模板；不持有当局布局或采集状态。</summary>
    public sealed class ValleyGenerationKit : MonoBehaviour
    {
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material waterMaterial;
        /// <summary>河槽与近岸泥沙表现；旧资源缺失时回退地表材质，不更改保存高度或碰撞。</summary>
        [SerializeField] private Material riverbedMaterial;
        [SerializeField] private CivilisationPointView[] searchTemplates;
        [SerializeField] private CivilisationPointView preparationTemplate;
        [SerializeField] private CivilisationPointView returnTemplate;
        /// <summary>沿路两段线索的纯表现模板；旧 Kit 未配置时沿用整备模板，身份仍来自保存的线索。</summary>
        [SerializeField] private CivilisationPointView transportClueTemplate, trailClueTemplate;
        [SerializeField] private GameObject campfirePrefab;
        [SerializeField] private GameObject awningPrefab;
        /// <summary>仅含正式美术的五种树形，顺序为树01～05；采集身份仍由 searchTemplates 提供。</summary>
        [SerializeField] private GameObject[] treeVisualPrefabs;
        /// <summary>无碰撞的低矮灌木、草簇来源；按块合批，不增加采集物或持久状态。</summary>
        [SerializeField] private GameObject[] shrubPrefabs;
        /// <summary>俯视地被草簇来源；只合并简化LOD网格，不实例化脚本或碰撞。</summary>
        [SerializeField] private GameObject[] grassPrefabs;
        /// <summary>样片转正式的无交互布景资源；全空兼容旧Kit，接入后必须完整，不承载采集身份。</summary>
        [SerializeField] private GameObject sceneryRockPrefab, sceneryGrassPrefab, sceneryShelterPrefab, coniferVisualPrefab, ridgeVisualPrefab;
        public Material TerrainMaterial => terrainMaterial;
        public Material WaterMaterial => waterMaterial;
        public Material RiverbedMaterial => riverbedMaterial != null ? riverbedMaterial : terrainMaterial;
        public CivilisationPointView PreparationTemplate => preparationTemplate;
        public CivilisationPointView ReturnTemplate => returnTemplate;
        public GameObject CampfirePrefab => campfirePrefab;
        public GameObject AwningPrefab => awningPrefab;
        /// <summary>按已保存的线索顺序选现场，不抽取新随机数或改变剧情内容。</summary>
        internal CivilisationPointView FindClueTemplate(int order) =>
            (order == 0 ? transportClueTemplate : trailClueTemplate) ?? preparationTemplate;
        internal GameObject[] TreeVisualPrefabs => treeVisualPrefabs;
        internal GameObject[] ShrubPrefabs => shrubPrefabs;
        internal GameObject[] GrassPrefabs => grassPrefabs;
        internal GameObject SceneryRockPrefab => sceneryRockPrefab;
        internal GameObject SceneryGrassPrefab => sceneryGrassPrefab;
        internal GameObject SceneryShelterPrefab => sceneryShelterPrefab;
        internal GameObject ConiferVisualPrefab => coniferVisualPrefab;
        internal GameObject RidgeVisualPrefab => ridgeVisualPrefab;
        public CivilisationPointView FindTemplate(string id) => searchTemplates?.FirstOrDefault(t=>t!=null&&t.PointId==id);
        /// <summary>候选创建后、正式可见前检查必需引用；旧Kit可全空回退，部分损坏的植被组明确失败。</summary>
        public bool Validate(out string error)
        {
            error=null;
            if(terrainMaterial==null||waterMaterial==null||preparationTemplate==null||returnTemplate==null||campfirePrefab==null||awningPrefab==null||
                searchTemplates==null||searchTemplates.Length==0||searchTemplates.Any(t=>t==null))
            {error="河谷生成资源模板缺少必要引用";return false;}
            // 全空允许旧版Kit保留原表现；开始接入任一植被组后，三个组必须完整，损坏引用不能静默跳过。
            var palettes = new[] { treeVisualPrefabs, shrubPrefabs, grassPrefabs };
            if (palettes.Any(p => p != null && p.Length > 0) && palettes.Any(p => p == null || p.Length == 0 || p.Any(v => v == null)))
            { error = "河谷植被资源组缺少必要引用"; return false; }
            var scenery=new[]{sceneryRockPrefab,sceneryGrassPrefab,sceneryShelterPrefab,coniferVisualPrefab,ridgeVisualPrefab};
            if(scenery.Any(p=>p!=null)&&scenery.Any(p=>p==null)){error="河谷样片布景资源缺少必要引用";return false;}
            return true;
        }
    }
}
