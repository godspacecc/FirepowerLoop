using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>背桶借用原敌人根；附属物仍由地图持有，切图先解除，避免跟随池中角色复用。</summary>
        private readonly Dictionary<int,CarrierBarrelVisual> carrierBarrels=new();
        /// <summary>固定桶和卸下桶共用同一正式资源及活动时钟。</summary>
        private readonly Dictionary<int,ArcadeVortexBarrelPresentation> groundBarrelVisuals=new();
        /// <summary>快照活动时刻；迟到的敌人实例从当前时刻采样。</summary>
        private double barrelVisualElapsed;
        /// <summary>附属物和借用根的绑定，不改变敌人归属或存活状态。</summary>
        private sealed class CarrierBarrelVisual
        {
            /// <summary>当前敌人实例的借用根，复用时必须重新核对身份。</summary>
            public Transform EnemyRoot;
            /// <summary>本地图拥有的背桶附属物。</summary>
            public GameObject Pack;
            /// <summary>该附属物的纯表现时钟采样器。</summary>
            public ArcadeVortexBarrelPresentation Effect;
        }
        /// <summary>同Content资源租约中的模板无需额外异步加载或运行时寻址。</summary>
        private GameObject CreateVortexBarrel(Transform parent,string name,bool carried)
        {
            var template=contentTemplates?.Find("VortexBarrelVisual");
            if(template==null)throw new InvalidOperationException("正式紫桶及浓雾模板未加载");
            var go=UnityEngine.Object.Instantiate(template.gameObject,parent,false);go.name=name;
            go.transform.Find("ShoulderStraps")?.gameObject.SetActive(carried);go.SetActive(true);return go;
        }
        /// <summary>依赖就绪后绑定原丧尸；迟到或复用的实例可在后续快照重试。</summary>
        private void SetCarrierBarrel(int id)
        {
            if(!IsRoundLayoutReady||enemyPresentation==null||contentTemplates==null)return;
            // 未出现或被回收的旧敌人不保留附属物；下一份真实快照可以重新绑定新实例。
            if(!enemyPresentation.TryGetEnemyPresentationRoot(id,out var enemy)||enemy==null){RemoveCarrierBarrel(id);return;}
            if(carrierBarrels.TryGetValue(id,out var current)&&(current.EnemyRoot!=enemy||current.Pack==null))
            {RemoveCarrierBarrel(id);current=null;}
            if(current==null)
            {
                var pack=CreateVortexBarrel(enemy,"CarrierBarrel_"+id,true);pack.transform.localPosition=new Vector3(0,.45f,-.4f);
                // 以静止挂点交给视觉根，迟到的背桶也跟随已经开始的攀爬。
                enemy.GetComponent<EnemyView>()?.AttachCarriedVisual(pack.transform);
                try{current=new CarrierBarrelVisual{EnemyRoot=enemy,Pack=pack,Effect=new ArcadeVortexBarrelPresentation(pack.transform)};}
                catch{pack.SetActive(false);DestroyObject(pack);throw;}
                carrierBarrels.Add(id,current);
            }
            current.Effect.Sample(barrelVisualElapsed);
        }
        /// <summary>只拆下背桶，不回收敌人本体。</summary>
        private void RemoveCarrierBarrel(int id)
        {
            if(!carrierBarrels.TryGetValue(id,out var entry))return;
            if(entry.Pack!=null){entry.Pack.SetActive(false);DestroyObject(entry.Pack);}carrierBarrels.Remove(id);
        }
        /// <summary>统一采样快照活动时刻，并清理已失效的借用根。</summary>
        private void SampleCarrierBarrels(double elapsed)
        {
            barrelVisualElapsed=elapsed;
            foreach(int id in carrierBarrels.Keys.ToArray())
            {
                var entry=carrierBarrels[id];
                if(entry.Pack==null||entry.EnemyRoot==null){RemoveCarrierBarrel(id);continue;}
                entry.Effect.Sample(elapsed);
            }
        }
        /// <summary>切图先解除轮级根之外的附属物，再清空地面桶采样缓存。</summary>
        private void ClearVortexBarrelVisuals()
        {
            foreach(int id in carrierBarrels.Keys.ToArray())RemoveCarrierBarrel(id);
            groundBarrelVisuals.Clear();barrelVisualElapsed=0;
        }
    }
}
