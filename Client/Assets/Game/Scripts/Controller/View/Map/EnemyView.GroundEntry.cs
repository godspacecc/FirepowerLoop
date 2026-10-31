using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class EnemyView
    {
        /// <summary>正式Prefab持有翻土材质；独立效果借用依赖，由地图释放。</summary>
        [SerializeField] private Material groundEntrySoilMaterial;
        private SurvivorGroundEntryState groundEntry;
        private Transform groundVisualRoot,groundHips;
        private Vector3 groundRestPosition,groundForward,groundUp;
        private Quaternion groundRestRotation;
        private bool groundSampling,groundAnimatorWasEnabled,groundColliderWasEnabled;
        /// <summary>由地图表现持有材质借用期，敌人只提供已预载依赖。</summary>
        public Material GroundEntrySoilMaterial => groundEntrySoilMaterial;
        /// <summary>地图准备事务检查正式依赖，缺失时拒绝交接，避免可见后才发现动作无效。</summary>
        public bool ValidateGroundEntry(out string error)
        {error=dockSeaEntryClip==null||groundEntrySoilMaterial==null?"丧尸爬出动作或翻土材质未准备完成":null;return error==null;}

        /// <summary>异步加载后直接采样当前阶段；重复及过期进度不会重播，不改变业务平面位置。</summary>
        public void SetGroundEntry(SurvivorGroundEntryState state,Vector3 forward,Vector3 up)
        {
            if(animationDead||isCorpse||!SurvivorGroundEntryRules.Valid(state)||
                groundEntry.Started&&state.Age<groundEntry.Age||!DockFinite(forward.sqrMagnitude)||!DockFinite(up.sqrMagnitude)||forward.sqrMagnitude<.001f||up.sqrMagnitude<.001f)return;
            groundEntry=state;
            if(!state.Active){FinishGroundEntry();return;}
            if(animator==null||dockSeaEntryClip==null||groundEntrySoilMaterial==null)return;
            if(groundVisualRoot==null)
            {
                groundVisualRoot=animator.transform.parent;
                if(groundVisualRoot==null||groundVisualRoot==transform){groundVisualRoot=null;return;}
                foreach(var bone in animator.GetComponentsInChildren<Transform>(true))if(bone.name=="Hips"){groundHips=bone;break;}
                if(groundHips==null){groundVisualRoot=null;return;}
                groundRestPosition=groundVisualRoot.localPosition;groundRestRotation=groundVisualRoot.localRotation;
            }
            groundForward=forward.normalized;groundUp=up.normalized;
            if(!groundSampling)
            {
                ResetMeleeAttack();ResetHerdImpact();groundAnimatorWasEnabled=animator.enabled;groundSampling=true;
                if(combatCollider==null)combatCollider=GetComponent<Collider>();
                groundColliderWasEnabled=combatCollider!=null&&combatCollider.enabled;
                if(combatCollider!=null)combatCollider.enabled=false;

            }
            animator.enabled=false;
        }
        /// <summary>复用已采用码头骨骼片段，视觉根绕髋前倾；不是只抬升整个模型。</summary>
        private void ApplyGroundEntry()
        {
            if(!groundSampling||animationDead||isCorpse)return;
            groundVisualRoot.SetLocalPositionAndRotation(groundRestPosition,groundRestRotation);
            transform.rotation=Quaternion.LookRotation(groundForward,groundUp);
            dockSeaEntryClip.SampleAnimation(animator.gameObject,SurvivorGroundEntryRules.ClipAge(groundEntry.Age));
            var hips=transform.InverseTransformPoint(groundHips.position);var pose=ArcadeDockSeaEntryRules.Sample(SurvivorGroundEntryRules.ClipAge(groundEntry.Age));
            var lean=Quaternion.Euler(pose.Tilt,0,0);groundVisualRoot.localRotation=lean*groundRestRotation;
            groundVisualRoot.localPosition=hips-lean*(hips-groundRestPosition)+Vector3.up*(pose.Height+SurvivorGroundEntryRules.ExtraDepth(groundEntry.Age));

        }
        private void FinishGroundEntry()
        {
            if(!groundSampling)return;
            RestoreGroundVisualRoot();groundSampling=false;animator.enabled=groundAnimatorWasEnabled;
            if(combatCollider!=null)combatCollider.enabled=groundColliderWasEnabled;
            if(animator.isActiveAndEnabled){animator.SetFloat(MoveSpeedAnimatorParameter,0);animator.Play("Run",0,0);animator.Update(0);}

        }
        /// <summary>死亡必须继承爬出的当前姿态，不先恢复站立；独立泥土由地图继续推进。</summary>
        private void StopGroundEntryForDeath()
        {
            if(!groundSampling)return;groundSampling=false;if(animator!=null)animator.enabled=groundAnimatorWasEnabled;

        }
        private void RestoreGroundVisualRoot(){if(groundVisualRoot!=null)groundVisualRoot.SetLocalPositionAndRotation(groundRestPosition,groundRestRotation);}
        private void ResetGroundEntry()
        {
            if(groundSampling&&animator!=null)animator.enabled=groundAnimatorWasEnabled;
            if(groundSampling&&combatCollider!=null)combatCollider.enabled=groundColliderWasEnabled;
            groundSampling=false;groundEntry=default;RestoreGroundVisualRoot();

        }
        /// <summary>翻土资源归地图持有；保留统一释放入口，不销毁地图效果。</summary>
        private void ReleaseGroundEntryResources() { }
    }
}
