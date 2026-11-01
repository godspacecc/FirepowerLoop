using System.Collections.Generic;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class ProjectileCtrl
    {
        /// <summary>地图拥有的敌弹及敌爆独立池，地图结束时先销毁再释放Prefab句柄。</summary>
        private readonly Dictionary<int,GameObject> hostileFireballs=new();
        private readonly Stack<GameObject> hostileFireballPool=new();
        private readonly Dictionary<int,GameObject> enemyBlasts=new();
        private readonly Stack<GameObject> enemyBlastPool=new();
        /// <summary>同步已裁决火球的完整姿态；复用时重置粒子，不执行碰撞或伤害。</summary>
        public void SetSurvivorHostileProjectile(int id,Vector3 position,Vector3 direction)
        {
            if(currentScene==null||!committedPrefabs.TryGetValue(arcadeProjectileAddress??"",out var prefab))return;
            if(!hostileFireballs.TryGetValue(id,out var instance))
            {
                while(hostileFireballPool.Count>0&&instance==null)instance=hostileFireballPool.Pop();
                if(instance==null)
                {
                    var template=prefab.Prefab.GetComponent<ProjectileView>().SurvivorHostileFireballTemplate;
                    if(template==null)return;
                    instance=this.GetUtility<IViewInstanceProvider>().Instantiate(template.transform,currentScene.DynamicObjectRoot,position,Quaternion.identity)?.gameObject;
                }
                if(instance==null)return;
                instance.transform.localScale=Vector3.one*.35f;instance.SetActive(true);
                foreach(var ps in instance.GetComponentsInChildren<ParticleSystem>(true)){ps.Clear(true);ps.Play(true);}
                hostileFireballs.Add(id,instance);
            }
            instance.transform.position=position;
            if(direction.sqrMagnitude>.0001f)instance.transform.rotation=Quaternion.LookRotation(direction.normalized);
        }
        /// <summary>命中、过期或停止时立即隐藏，并归还本地图敌弹池。</summary>
        public void RemoveSurvivorHostileProjectile(int id)
        {if(!hostileFireballs.Remove(id,out var o)||o==null)return;o.SetActive(false);hostileFireballPool.Push(o);}
        /// <summary>独立敌爆预算只控制表现；按权威进度采样既有爆燃资源。</summary>
        public void SetSurvivorEnemyBlast(int id,Vector3 position,float radius,float progress,int capacity)
        {
            if(currentScene==null||capacity<=0||!committedPrefabs.TryGetValue(arcadeProjectileAddress??"",out var prefab))return;
            if(!enemyBlasts.TryGetValue(id,out var instance))
            {
                if(enemyBlasts.Count>=capacity)return;
                while(enemyBlastPool.Count>0&&instance==null)instance=enemyBlastPool.Pop();
                if(instance==null)
                {
                    var template=prefab.Prefab.GetComponent<ProjectileView>().SurvivorEnemyBlastTemplate;
                    if(template==null)return;
                    instance=this.GetUtility<IViewInstanceProvider>().Instantiate(template.transform,currentScene.DynamicObjectRoot,position,Quaternion.identity)?.gameObject;
                }
                if(instance==null)return;enemyBlasts.Add(id,instance);instance.gameObject.SetActive(true);
            }
            instance.transform.position=position+Vector3.up*.4f;instance.transform.localScale=Vector3.one*(radius/1.5f*.8f);
            foreach(var ps in instance.GetComponentsInChildren<ParticleSystem>(true)){ps.Simulate(Mathf.Clamp01(progress)*.4f,false,true,true);ps.Pause();}
        }
        /// <summary>爆燃表现结束后归还独立池，不触碰玩家强化爆炸。</summary>
        public void RemoveSurvivorEnemyBlast(int id)
        {if(!enemyBlasts.Remove(id,out var o)||o==null)return;o.gameObject.SetActive(false);enemyBlastPool.Push(o);}
        /// <summary>所有地图释放入口先销毁活动与空闲实例，再释放其Prefab依赖。</summary>
        private void ClearSurvivorSpecialVisuals()
        {
            var provider=this.GetUtility<IViewInstanceProvider>();
            foreach(var o in hostileFireballs.Values)if(o!=null)provider.Release(o);
            hostileFireballs.Clear();while(hostileFireballPool.Count>0){var o=hostileFireballPool.Pop();if(o!=null)provider.Release(o);}
            foreach(var o in enemyBlasts.Values)if(o!=null)provider.Release(o.gameObject);
            enemyBlasts.Clear();while(enemyBlastPool.Count>0){var o=enemyBlastPool.Pop();if(o!=null)provider.Release(o.gameObject);}
        }
    }
}

