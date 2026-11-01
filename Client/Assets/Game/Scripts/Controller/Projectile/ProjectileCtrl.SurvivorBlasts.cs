using System.Collections.Generic;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class ProjectileCtrl
    {
        /// <summary>爆炸实例随地图事务回收；有界池不影响权威伤害。</summary>
        private readonly Dictionary<int,SurvivorBlastView> survivorBlasts=new();
        private readonly Stack<SurvivorBlastView> survivorBlastPool=new();
        public void SetSurvivorBlast(int id,Vector3 position,float radius,float progress,int capacity)
        {
            if(currentScene==null||capacity<=0||!committedPrefabs.TryGetValue(arcadeProjectileAddress??"",out var prefab))return;
            if(!survivorBlasts.TryGetValue(id,out var instance))
            {
                if(survivorBlasts.Count>=capacity)return;
                while(survivorBlastPool.Count>0&&instance==null)instance=survivorBlastPool.Pop();
                if(instance==null)
                {
                    var template=prefab.Prefab.GetComponent<ProjectileView>().SurvivorBlastTemplate;
                    if(template==null)return;
                    instance=this.GetUtility<IViewInstanceProvider>().Instantiate(template,currentScene.DynamicObjectRoot,position,Quaternion.identity);
                }
                if(instance==null)return;
                survivorBlasts.Add(id,instance);instance.gameObject.SetActive(true);
            }
            instance.Apply(position,radius,progress);
        }
        public void RemoveSurvivorBlast(int id)
        {
            if(!survivorBlasts.Remove(id,out var instance)||instance==null)return;
            instance.gameObject.SetActive(false);survivorBlastPool.Push(instance);
        }
        /// <summary>在资源句柄释放前销毁活动与空闲实例，禁止跨地图残留。</summary>
        private void ClearSurvivorBlasts()
        {
            foreach(var instance in survivorBlasts.Values)if(instance!=null)this.GetUtility<IViewInstanceProvider>().Release(instance.gameObject);
            survivorBlasts.Clear();
            while(survivorBlastPool.Count>0){var instance=survivorBlastPool.Pop();if(instance!=null)this.GetUtility<IViewInstanceProvider>().Release(instance.gameObject);}
        }
    }
}
