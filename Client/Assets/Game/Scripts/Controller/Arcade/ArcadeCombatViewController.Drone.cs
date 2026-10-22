using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>在无人机高度显示支援弹丸；不改规则平面碰撞。</summary>
        public void SetDroneProjectile(int id,Vector2 position,Vector2 direction,string resource,float height)
        {
            if(projectilePresentation==null||mapRoot==null)return;
            projectilePresentation.TrySetProjectile(id,mapRoot.TransformPoint(World(position,height)),
                mapRoot.TransformDirection(new Vector3(direction.x,0,direction.y)),resource,false,out _);
        }

        /// <summary>复用已有异步效果资源事务，切换阶段时释放旧机体，爆炸不拥有伤害逻辑。</summary>
        public void SetDrone(long ownerPlayerId,ArcadePowerupDefinition definition,Vector2 position,Vector2 direction,int phase,int powerTier)
        {
            string key=$"powerup:{ownerPlayerId}:{(int)definition.Behavior}";
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
