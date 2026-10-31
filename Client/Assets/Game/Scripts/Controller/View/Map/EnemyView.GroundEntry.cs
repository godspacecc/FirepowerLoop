using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class EnemyView
    {
        /// <summary>正式Prefab依赖持有翻土材质；每个池中实例只创建并复用一张小网格。</summary>
        [SerializeField] private Material groundEntrySoilMaterial;
        private SurvivorGroundEntryState groundEntry;
        private Transform groundVisualRoot,groundHips;
        private Vector3 groundRestPosition,groundForward,groundUp,groundSoilOrigin;
        private Quaternion groundRestRotation;
        private bool groundSampling,groundAnimatorWasEnabled,groundColliderWasEnabled;
        private GameObject groundSoilObject;
        private Mesh groundSoilMesh;
        private MeshRenderer groundSoilRenderer;
        private Vector3[] groundSoilVertices;
        /// <summary>与采用版相同的二十面体土块顶点，全部实例共用只读形状。</summary>
        private static readonly Vector3[] GroundClodShape=CreateGroundClodShape();
        private static Vector3[] CreateGroundClodShape()
        {
            float t=(1+Mathf.Sqrt(5))/2;var v=new[]{new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)};
            int[] triangles={0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};var result=new Vector3[triangles.Length];for(int i=0;i<result.Length;i++)result[i]=v[triangles[i]].normalized;return result;
        }
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
                groundSoilOrigin=transform.position-groundForward*SurvivorGroundEntryRules.CrawlDistance(state.Age);
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
            SampleGroundSoil();
        }
        private void FinishGroundEntry()
        {
            if(!groundSampling)return;
            RestoreGroundVisualRoot();groundSampling=false;animator.enabled=groundAnimatorWasEnabled;
            if(combatCollider!=null)combatCollider.enabled=groundColliderWasEnabled;
            if(animator.isActiveAndEnabled){animator.SetFloat(MoveSpeedAnimatorParameter,0);animator.Play("Run",0,0);animator.Update(0);}
            if(groundSoilObject!=null)groundSoilObject.SetActive(false);
        }
        /// <summary>死亡必须继承爬出的当前姿态，不先恢复站立；土块同步收起。</summary>
        private void StopGroundEntryForDeath()
        {
            if(!groundSampling)return;groundSampling=false;if(animator!=null)animator.enabled=groundAnimatorWasEnabled;
            if(groundSoilObject!=null)groundSoilObject.SetActive(false);
        }
        private void RestoreGroundVisualRoot(){if(groundVisualRoot!=null)groundVisualRoot.SetLocalPositionAndRotation(groundRestPosition,groundRestRotation);}
        private void ResetGroundEntry()
        {
            if(groundSampling&&animator!=null)animator.enabled=groundAnimatorWasEnabled;
            if(groundSampling&&combatCollider!=null)combatCollider.enabled=groundColliderWasEnabled;
            groundSampling=false;groundEntry=default;RestoreGroundVisualRoot();
            if(groundSoilObject!=null)groundSoilObject.SetActive(false);
        }
        /// <summary>一张合并网格包含土面和十块小土；沿权威年龄抖动，无粒子或逐物件Update。</summary>
        private void SampleGroundSoil()
        {
            const int count=11;
            if(groundSoilObject==null)
            {
                groundSoilObject=new GameObject("GroundEntrySoil",typeof(MeshFilter),typeof(MeshRenderer));groundSoilObject.transform.SetParent(transform,false);groundSoilObject.layer=gameObject.layer;
                groundSoilMesh=new Mesh{name="GroundEntrySoil"};groundSoilMesh.MarkDynamic();groundSoilVertices=new Vector3[count*GroundClodShape.Length];var triangles=new int[groundSoilVertices.Length];
                for(int i=0;i<triangles.Length;i++)triangles[i]=i;
                groundSoilMesh.vertices=groundSoilVertices;groundSoilMesh.triangles=triangles;
                groundSoilObject.GetComponent<MeshFilter>().sharedMesh=groundSoilMesh;groundSoilRenderer=groundSoilObject.GetComponent<MeshRenderer>();groundSoilRenderer.sharedMaterial=groundEntrySoilMaterial;
            }
            groundSoilObject.SetActive(true);groundSoilObject.transform.position=groundSoilOrigin;
            // 采用版二十面体低多边形土块；只更新固定顶点缓冲。
            for(int i=0;i<count;i++)
            {
                float angle=(i-1)*Mathf.PI*2/10;float r=.65f+Mathf.Sin(Mathf.Clamp01(groundEntry.Age/SurvivorGroundEntryRules.TotalSeconds)*Mathf.PI)*.15f;
                var center=i==0?new Vector3(0,.03f,0):new Vector3(Mathf.Cos(angle)*r,.06f+Mathf.Abs(Mathf.Sin(groundEntry.Age*15+i))*.09f,Mathf.Sin(angle)*r);
                var scale=i==0?new Vector3(.8f,.06f,.7f):new Vector3(.12f,.1f,.14f);
                for(int j=0;j<GroundClodShape.Length;j++)groundSoilVertices[i*GroundClodShape.Length+j]=center+Vector3.Scale(GroundClodShape[j],scale);
            }
            groundSoilMesh.vertices=groundSoilVertices;groundSoilMesh.RecalculateNormals();groundSoilMesh.RecalculateBounds();
        }
        private void ReleaseGroundEntryResources()
        {
            if(groundSoilObject!=null){if(Application.isPlaying)Destroy(groundSoilObject);else DestroyImmediate(groundSoilObject);}
            if(groundSoilMesh!=null){if(Application.isPlaying)Destroy(groundSoilMesh);else DestroyImmediate(groundSoilMesh);}
            groundSoilObject=null;groundSoilMesh=null;
        }
    }
}
