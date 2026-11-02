using System.Linq;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>当前任务箱加载错误；结束战场时清除，不允许失败后隐形收集。</summary>
        private string missionSupplyLoadError;
        public string MissionSupplyLoadError=>missionSupplyLoadError;
        /// <summary>沿用已有资源句柄事务，已收集或退图时释放同一任务箱。</summary>
        public void SetMissionSupply(int id,bool visible,Vector2 position)
        {
            string key="mission-supply:"+id;
            if(!visible){ReleaseEffectVisual(key);return;}
            if(!string.IsNullOrEmpty(missionSupplyLoadError))return;
            SetEffectVisual(key,key,"Prefabs/SceneObjects/TestResourcePickup",position,Vector3.zero);
            if(effectVisuals.TryGetValue(key,out var entry))ApplyMissionSupplyAppearance(entry);
        }
        /// <summary>复用同一地图资源事务预载出口，隐藏期间也保留句柄。</summary>
        public void SetMissionExit(bool visible,Vector2 position)
        {
            const string key="mission-exit";
            if(!string.IsNullOrEmpty(missionSupplyLoadError))return;
            SetEffectVisual(key,key,"Prefabs/SceneObjects/MissionExitVisual",position,new Vector3(0,.04f,0));
            if(effectVisuals.TryGetValue(key,out var entry)){entry.MissionExitVisible=visible;ApplyMissionExitAppearance(entry);}
        }
        /// <summary>真实实例存在才允许完成撤离。</summary>
        public bool IsMissionExitReady=>effectVisuals.TryGetValue("mission-exit",out var entry)&&entry.Instance!=null;
        /// <summary>包括迟到回调在内，都按最新任务阶段显示静态圆环。</summary>
        private static void ApplyMissionExitAppearance(EffectVisualEntry entry)
        {
            if(entry.IsMissionExit&&entry.Instance!=null)entry.Instance.SetActive(entry.MissionExitVisible);
        }
        /// <summary>只创建采用版柴油机的纯显示实例，不附带旧电塔行为。</summary>
        public void SetMissionFacility(Vector2 position)
        {
            if(!string.IsNullOrEmpty(missionSupplyLoadError))return;
            SetEffectVisual("mission-facility","mission-facility","Prefabs/SceneObjects/MissionGeneratorVisual",position,Vector3.zero);
        }
        /// <summary>设施须加载完毕才允许入场，避免在不可见目标处防守。</summary>
        public bool IsMissionFacilityReady=>effectVisuals.TryGetValue("mission-facility",out var entry)&&entry.Instance!=null;
        /// <summary>入口必须等到实例实际存在，不能把请求发出视为准备完成。</summary>
        public bool IsMissionSupplyReady(int id)=>effectVisuals.TryGetValue("mission-supply:"+id,out var entry)&&entry.Instance!=null;
        /// <summary>只克隆已确认箱体子树，排除根触发器、拾取行为、名称和无关模型。</summary>
        private static GameObject MissionSupplyTemplate(GameObject prefab)=>prefab==null?null:prefab.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="WeaponDeliveryCrate")?.gameObject;
        /// <summary>首次及迟到加载均恢复预览采用的原尺寸闭合箱体，不播放空投动作。</summary>
        private static void ApplyMissionSupplyAppearance(EffectVisualEntry entry)
        {
            if(!entry.IsMissionSupply||entry.Instance==null)return;
            entry.Instance.transform.localScale=Vector3.one;
            entry.Instance.SetActive(true);
        }
    }
}
