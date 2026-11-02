using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>将局部爆心转换到地图坐标，表现池由既有投射物控制器拥有。</summary>
        public void SetSurvivorBlast(int id,Vector2 position,float radius,float progress,int capacity)
        {
            if(projectilePresentation==null||mapRoot==null)return;
            projectilePresentation.SetSurvivorBlast(id,mapRoot.TransformPoint(World(position,0)),radius,progress,capacity);
        }
        public void RemoveSurvivorBlast(int id)=>projectilePresentation?.RemoveSurvivorBlast(id);
        /// <summary>在无人机高度显示支援弹丸；不改规则平面碰撞。</summary>
        public void SetDroneProjectile(int id,Vector2 position,Vector2 direction,string resource,float height)
        {
            if(projectilePresentation==null||mapRoot==null)return;
            projectilePresentation.TrySetProjectile(id,mapRoot.TransformPoint(World(position,height)),
                mapRoot.TransformDirection(new Vector3(direction.x,0,direction.y)),resource,false,.55f,out _);
        }

        /// <summary>复用已有异步效果资源事务，切换阶段时释放旧机体，爆炸不拥有伤害逻辑。</summary>
        public void SetDrone(long ownerPlayerId,ArcadePowerupDefinition definition,Vector2 position,Vector2 direction,int phase,int powerTier)
            =>SetDroneUnit(ownerPlayerId,0,definition,position,direction,phase,powerTier);

        /// <summary>同一玩家的两架无人机各自持有资源事务，降级或过期由效果同步独立回收。</summary>
        public void SetDroneUnit(long ownerPlayerId,int unitIndex,ArcadePowerupDefinition definition,Vector2 position,Vector2 direction,int phase,int powerTier)
        {
            if(unitIndex<0||unitIndex>1)return;
            string key=$"powerup:{ownerPlayerId}:{(int)definition.Behavior}"+(unitIndex==0?"":":1");
            if(phase==(int)ArcadeDronePhase.Expired){ReleaseEffectVisual(key);return;}
            bool exploded=phase==(int)ArcadeDronePhase.Exploding;
            string address=definition.ActiveVisualPrefabAddress;
            if(exploded&&!ResourceAddress.TryFromFileName(ResourceAddressKind.SceneObjectPrefab,"ArcadeDroneExplosion",false,out address,out _))return;
            SetEffectVisual(key,definition.Id,address,position,exploded?new Vector3(0,.8f,0):definition.VisualOffset);
            if(!effectVisuals.TryGetValue(key,out var entry))return;
            entry.IsDrone=!exploded;entry.DronePowerTier=Mathf.Clamp(powerTier,1,3);
            if(!exploded&&direction.sqrMagnitude>.000001f)
            {entry.LocalRotation=Quaternion.LookRotation(new Vector3(direction.x,0,direction.y));entry.HasShotDirection=true;}
            if(entry.Instance==null)return;
            if(entry.HasShotDirection)entry.Instance.transform.localRotation=entry.LocalRotation;
            if(entry.IsDrone)ApplyDroneAppearance(entry);
        }

        private static readonly string SurvivorDroneKey=$"powerup:1:{((int)global::ArcadePowerupBehavior.Drone)}";

        /// <summary>只在幸存者无人机实例上绑定一次，普通机体与改装使用相同的资源租约。</summary>
        private static void BindDroneNozzle(EffectVisualEntry entry)
        {
            if(entry.DisplayName!="survivor.drone")return;
            entry.Nozzle=entry.Instance.transform.Find("FlameRetrofit")?.gameObject;
            entry.NozzleFlame=entry.Nozzle?.GetComponentInChildren<ParticleSystem>(true);
            if(entry.NozzleFlame==null)throw new System.InvalidOperationException("幸存者无人机缺少喷火改装组件");
            SampleDroneNozzle(entry,0);
        }

        /// <summary>机体位置和朝向由 SetDrone 统一投影；是否装备与是否开火独立控制。</summary>
        public void SetDroneFlame(bool upgraded,Vector2 position,Vector2 direction,bool emitting,float dt)
        {
            if(!effectVisuals.TryGetValue(SurvivorDroneKey,out var entry) || entry.DisplayName!="survivor.drone")return;
            entry.NozzleEnabled=upgraded;
            entry.NozzleEmitting=upgraded&&emitting;
            if(entry.Instance!=null && entry.Nozzle==null)BindDroneNozzle(entry);
            SampleDroneNozzle(entry,dt);
        }

        /// <summary>喷嘴只在改装后显示；停火清空粒子，暂停没有采样，不推进表现。</summary>
        private static void SampleDroneNozzle(EffectVisualEntry entry,float dt)
        {
            if(entry.Nozzle==null)return;
            if(entry.Nozzle.activeSelf!=entry.NozzleEnabled)entry.Nozzle.SetActive(entry.NozzleEnabled);
            if(!entry.NozzleEmitting)
            {
                if(entry.FlameStarted)entry.NozzleFlame.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                entry.FlameStarted=false;
                return;
            }
            entry.NozzleFlame.Simulate(dt,false,!entry.FlameStarted,false);
            entry.FlameStarted=true;
        }

        /// <summary>普通、红、紫按已确认预览着色；恢复普通时清除覆盖，不实例化共享材质。</summary>
        private static void ApplyDroneAppearance(EffectVisualEntry entry)
        {
            foreach(var renderer in entry.Instance.GetComponentsInChildren<Renderer>(true))
            {
                if(entry.DronePowerTier<=1){renderer.SetPropertyBlock(null);continue;}
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                Color tint=entry.DronePowerTier==2?new Color(1,.2f,.15f):new Color(.7f,.15f,1);
                block.SetColor("_BaseColor",Color.Lerp(Color.white,tint,.55f));renderer.SetPropertyBlock(block);
            }
        }
    }
}
