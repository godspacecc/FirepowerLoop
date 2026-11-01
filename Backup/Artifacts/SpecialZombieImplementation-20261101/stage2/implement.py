from pathlib import Path
import uuid
root=Path('Client/Assets/Game/Scripts')
def edit(p,old,new):
 s=p.read_text(encoding='utf-8-sig'); assert old in s,str(p);p.write_text(s.replace(old,new),encoding='utf-8')
def create(p,s):
 p.write_text(s,encoding='utf-8');Path(str(p)+'.meta').write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='utf-8')



create(root/'Controller/View/Map/EnemyView.SpecialZombies.cs','''using UnityEngine;
namespace Game
{
    public sealed partial class EnemyView
    {
        /// <summary>预载于独立特殊敌人Prefab的地面红圈，只有权威前摇才显示。</summary>
        [SerializeField] private LineRenderer survivorWarning;
        private bool survivorCasting;
        /// <summary>消费完整权威快照；红圈不计时、不判定伤害，朝向覆盖追踪。</summary>
        public void SetSurvivorSpecial(string kind,Vector3 direction,bool warning,float progress,float radius)
        {
            if(direction.sqrMagnitude>.0001f)transform.rotation=Quaternion.LookRotation(direction.normalized,transform.up);
            if(survivorWarning!=null)
            {
                survivorWarning.enabled=warning&&kind=="Exploder";
                survivorWarning.transform.localScale=Vector3.one*Mathf.Max(0,radius);
                var c=new Color(1,.15f,.03f,Mathf.Lerp(.45f,1,Mathf.Clamp01(progress)));
                survivorWarning.startColor=survivorWarning.endColor=c;
            }
            // 复用现有按权威阶段采样的抬手动作，不从Animator反向裁决攻击。
            if(kind=="Caster")
            {
                if(warning)SetMeleeAttack(1,1-Mathf.Clamp01(progress),0,direction);
                else if(survivorCasting)SetMeleeAttack(0,0,0,direction);
                survivorCasting=warning;
            }
        }
        /// <summary>死亡、休眠与回池必须立即关闭前摇，禁止下一身份继承。</summary>
        private void ResetSurvivorSpecial()
        { if(survivorWarning!=null)survivorWarning.enabled=false;survivorCasting=false; }
    }
}
''')
p=root/'Controller/Projectile/ProjectileRuntimePorts.cs';edit(p,'        void RemoveProjectile(int runtimeId);','        /// <summary>敌弹与敌爆具有独立实例及容量，不占用玩家强化表现池。</summary>\n        void SetSurvivorHostileProjectile(int id,Vector3 position,Vector3 direction) { }\n        void RemoveSurvivorHostileProjectile(int id) { }\n        void SetSurvivorEnemyBlast(int id,Vector3 position,float radius,float progress,int capacity) { }\n        void RemoveSurvivorEnemyBlast(int id) { }\n        void RemoveProjectile(int runtimeId);')
p=root/'Controller/View/Combat/ProjectileView.cs';edit(p,'        public SurvivorBlastView SurvivorBlastTemplate => survivorBlastTemplate;','        public SurvivorBlastView SurvivorBlastTemplate => survivorBlastTemplate;\n        /// <summary>随弹丸准备事务加载的敌方法术火球依赖。</summary>\n        [SerializeField] private GameObject survivorHostileFireballTemplate;\n        public GameObject SurvivorHostileFireballTemplate => survivorHostileFireballTemplate;')
p=root/'Controller/Projectile/ProjectileCtrl.cs';edit(p,'            ClearSurvivorBlasts();','            ClearSurvivorSpecialVisuals();\n            ClearSurvivorBlasts();')
create(root/'Controller/Projectile/ProjectileCtrl.SpecialZombies.cs','''using System.Collections.Generic;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class ProjectileCtrl
    {
        /// <summary>地图拥有的敌弹及敌爆独立池，地图结束时先销毁再释放Prefab句柄。</summary>
        private readonly Dictionary<int,GameObject> hostileFireballs=new();
        private readonly Stack<GameObject> hostileFireballPool=new();
        private readonly Dictionary<int,SurvivorBlastView> enemyBlasts=new();
        private readonly Stack<SurvivorBlastView> enemyBlastPool=new();
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
                    instance=this.GetUtility<IViewInstanceProvider>().Instantiate(template,currentScene.DynamicObjectRoot,position,Quaternion.identity);
                }
                if(instance==null)return;
                instance.transform.localScale=Vector3.one*.35f;instance.SetActive(true);
                foreach(var ps in instance.GetComponentsInChildren<ParticleSystem>(true)){ps.Clear(true);ps.Play(true);}
                hostileFireballs.Add(id,instance);
            }
            instance.transform.position=position;
            if(direction.sqrMagnitude>.0001f)instance.transform.rotation=Quaternion.LookRotation(direction.normalized);
        }
        public void RemoveSurvivorHostileProjectile(int id)
        {if(!hostileFireballs.Remove(id,out var o)||o==null)return;o.SetActive(false);hostileFireballPool.Push(o);}
        public void SetSurvivorEnemyBlast(int id,Vector3 position,float radius,float progress,int capacity)
        {
            if(currentScene==null||capacity<=0||!committedPrefabs.TryGetValue(arcadeProjectileAddress??"",out var prefab))return;
            if(!enemyBlasts.TryGetValue(id,out var instance))
            {
                if(enemyBlasts.Count>=capacity)return;
                while(enemyBlastPool.Count>0&&instance==null)instance=enemyBlastPool.Pop();
                if(instance==null)
                {
                    var template=prefab.Prefab.GetComponent<ProjectileView>().SurvivorBlastTemplate;
                    if(template==null)return;
                    instance=this.GetUtility<IViewInstanceProvider>().Instantiate(template,currentScene.DynamicObjectRoot,position,Quaternion.identity);
                }
                if(instance==null)return;enemyBlasts.Add(id,instance);instance.gameObject.SetActive(true);
            }
            instance.Apply(position,radius,progress);
        }
        public void RemoveSurvivorEnemyBlast(int id)
        {if(!enemyBlasts.Remove(id,out var o)||o==null)return;o.gameObject.SetActive(false);enemyBlastPool.Push(o);}
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
''')
create(root/'Controller/Arcade/ArcadeCombatViewController.SpecialZombies.cs','''using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>完整状态在普通追踪后投影，盾牌朝向始终由规则方向决定。</summary>
        public void SetSurvivorEnemySpecial(int id,string kind,Vector2 direction,bool warning,float progress,Vector2 target,float radius)
        {
            if(mapRoot==null)return;
            enemyPresentation?.SetSurvivorSpecial(id,kind,mapRoot.TransformDirection(World(direction,0)),warning,progress,radius);
        }
        public void SetSurvivorHostileProjectile(int id,Vector2 position,Vector2 direction)
        {if(mapRoot!=null)projectilePresentation?.SetSurvivorHostileProjectile(id,mapRoot.TransformPoint(World(position,1.1f)),mapRoot.TransformDirection(World(direction,0)));}
        public void RemoveSurvivorHostileProjectile(int id)=>projectilePresentation?.RemoveSurvivorHostileProjectile(id);
        public void SetSurvivorEnemyBlast(int id,Vector2 position,float radius,float progress,int capacity)
        {if(mapRoot!=null)projectilePresentation?.SetSurvivorEnemyBlast(id,mapRoot.TransformPoint(World(position,0)),radius,progress,capacity);}
        public void RemoveSurvivorEnemyBlast(int id)=>projectilePresentation?.RemoveSurvivorEnemyBlast(id);
    }
}
''')

