using System;using System.Linq;using UnityEngine;
namespace Game
{
    /// <summary>幸存者采用版受击：只叠加骨骼及有限子节点，由演员既有反馈帧推进。</summary>
    [DisallowMultipleComponent]
    public sealed class SurvivorHitFeedbackView : MonoBehaviour
    {
        /// <summary>正式模板的固定亮点和五滴液滴，命中时复用。</summary>
        [SerializeField]private Transform contactFlash;
        [SerializeField]private Transform[] droplets=Array.Empty<Transform>();
        /// <summary>动画骨架与本次叠加基线，不作为移动或伤害状态。</summary>
        private Transform chest,head;
        private Quaternion chestBefore,chestAfter,headBefore,headAfter;
        private bool poseApplied,player;
        /// <summary>仅由活动反馈时钟推进的年龄及本次世界推力。</summary>
        private float age=1f;
        private Vector3 push,contact;
        private float Duration=>player?.24f:.30f;
        /// <summary>正式预载验证依赖，缺少模板时在准备阶段失败而非首发命中时报错。</summary>
        public bool Validate(out string error)
        {
            error=null;
            if(contactFlash==null||droplets.Length!=5||Array.Exists(droplets,t=>t==null)){error="受击模板节点不完整";return false;}
            foreach(var renderer in GetComponentsInChildren<Renderer>(true))
                foreach(var material in renderer.sharedMaterials)if(material==null||material.shader==null){error="受击模板材质缺失";return false;}
            return true;
        }
        /// <summary>只在模式装配时缓存正式骨骼，不在每发命中时遍历。</summary>
        public void Bind(Animator animator,bool isPlayer)
        {
            Clear();player=isPlayer;
            if(animator==null)throw new InvalidOperationException("幸存者受击缺少动画骨架");
            var rig=animator.GetComponentsInChildren<Transform>(true);
            chest=rig.FirstOrDefault(t=>t.name==(player?"Spine_02":"Torso"));
            if(chest==null&&animator.isHuman)chest=animator.GetBoneTransform(HumanBodyBones.Chest);
            head=rig.FirstOrDefault(t=>t.name=="Head"||t.name=="Head_01");
            if(head==null&&animator.isHuman)head=animator.GetBoneTransform(HumanBodyBones.Head);
            if(chest==null||contactFlash==null||droplets.Length!=5||Array.Exists(droplets,t=>t==null))
                throw new InvalidOperationException("幸存者受击模板或躯干绑定不完整");
        }
        /// <summary>推力来自本次权威命中；忽略无法确定方向的零向量，不以角色朝向猜测。</summary>
        public void Play(Vector3 worldPush)
        {
            worldPush.y=0;
            if(chest==null||!Finite(worldPush)||worldPush.sqrMagnitude<.0001f)return;
            RestorePose();push=worldPush.normalized;age=0;
            contact=chest.position-push*.18f+Vector3.up*.05f;
            Render();
        }
        private static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.z);
        /// <summary>先撤销上一帧叠加；零活动时间不消耗表现，也不叠加偏转。</summary>
        public void Tick(float seconds){RestorePose();age+=Mathf.Max(0,seconds);Render();}
        /// <summary>快速受力、较慢归位、末段小幅反向回弹，与已采用第三版相同。</summary>
        private float Envelope()
        {
            if(age>=Duration)return 0;float u=age/Duration;
            return u<.25f?Mathf.Sin(u/.25f*Mathf.PI*.5f):u<.78f?Mathf.Cos((u-.25f)/.53f*Mathf.PI*.5f):-.16f*Mathf.Sin((u-.78f)/.22f*Mathf.PI);
        }
        /// <summary>动画、抓挠和握持完成后施加世界推力；战斗根、腿部和碰撞均不修改。</summary>
        public void ApplyPose()
        {
            RestorePose();if(chest==null||age>=Duration)return;
            var axis=Vector3.Cross(Vector3.up,push);float amount=Envelope();
            chestBefore=chest.localRotation;chest.rotation=Quaternion.AngleAxis(amount*(player?12:30),axis)*chest.rotation;chestAfter=chest.localRotation;
            if(head!=null){headBefore=head.localRotation;head.rotation=Quaternion.AngleAxis(amount*(player?5:9),axis)*head.rotation;headAfter=head.localRotation;}
            poseApplied=true;
        }
        /// <summary>只恢复仍属于本次叠加的局部值，Animator已重采样时不回写旧动作。</summary>
        public void RestorePose()
        {
            if(!poseApplied)return;
            if(head!=null&&Quaternion.Angle(head.localRotation,headAfter)<.01f)head.localRotation=headBefore;
            if(chest!=null&&Quaternion.Angle(chest.localRotation,chestAfter)<.01f)chest.localRotation=chestBefore;
            poseApplied=false;
        }
        /// <summary>复用接触亮点与五滴短时液滴，不产生额外对象或碰撞体。</summary>
        private void Render()
        {
            if(contactFlash==null)return;
            contactFlash.gameObject.SetActive(age<.067f);contactFlash.position=contact;
            contactFlash.localScale=Vector3.one*(player?.075f:.11f)*(1+Mathf.Clamp01(age/.067f)*.3f);
            for(int j=0;j<droplets.Length;j++)
            {
                var d=droplets[j];d.gameObject.SetActive(!player&&age<.22f);
                d.position=contact+push*(age*(.6f+j*.22f))+Vector3.up*(age*(.8f+j*.13f)-age*age*7);
                d.localScale=new Vector3(.025f,.045f,.025f)*(1-Mathf.Clamp01(age/.3f));
            }
        }
        /// <summary>死亡、禁用、回池及模式退出统一清理，子节点随原演员资源一起释放。</summary>
        public void Clear(){RestorePose();age=1;if(contactFlash!=null)contactFlash.gameObject.SetActive(false);foreach(var d in droplets)if(d!=null)d.gameObject.SetActive(false);}
        private void OnDisable()=>Clear();
    }
}
