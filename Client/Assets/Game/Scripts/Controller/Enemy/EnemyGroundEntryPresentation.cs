using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
namespace Game
{
    /// <summary>当前地图独占的翻土表现；敌人仅触发与同步年龄，回池不影响已发生的落土。</summary>
    public sealed class EnemyGroundEntryPresentation : IDisposable
    {
        /// <summary>只借用本地图根，不保存玩家或敌人权威状态。</summary>
        private readonly Transform parent; // 当前地图表现根，不挂在敌人子树。
        private readonly Dictionary<int, Entry> entries = new(); // 本地图运行时身份，禁止跨图复用。
        private readonly Mesh shape; // 本地图共享只读土块网格。
        private readonly List<int> expired = new(); // 复用清理缓冲，不在枚举时修改字典。
        private sealed class Entry
        {
            public GameObject Root; // 独立世界位置的效果根。
            public Transform Mark; // 洞口土面，固定留在原地。
            public Renderer[] DustRenderers; // 缓存渲染器，采样不重复查组件。
            public Transform[] Clods, Dust; // 采用版28土块与18尘团。
            public Material Earth, Dark, DustMaterial; // 借用材质的实例副本，独立释放。
            public float Age, StopAge = float.PositiveInfinity; // 中断后只结束新喷发，继续旧轨迹。
            public bool Tail; // 身体结束后由地图表现时钟推进收尾。
        }
        /// <summary>创建本地图共享形状；不查询动态资源地址。</summary>
        public EnemyGroundEntryPresentation(Transform parent)
        {
            this.parent=parent;
            var vertices=CreateGroundClodShape();shape=new Mesh{name="GroundEntryClod"};shape.vertices=vertices;
            var indices=new int[vertices.Length];for(int i=0;i<indices.Length;i++)indices[i]=i;
            shape.triangles=indices;shape.RecalculateNormals();shape.RecalculateBounds();
        }
        /// <summary>只接受本地图的真实演员；迟到的结束快照不补播已完成的出土。</summary>
        public void Sync(int id, EnemyView view, SurvivorGroundEntryState state, Vector3 forward, Vector3 up)
        {
            if(parent==null||view==null||!view.transform.IsChildOf(parent)||!SurvivorGroundEntryRules.Valid(state)||view.GroundEntrySoilMaterial==null)return;
            if(!entries.TryGetValue(id,out var e))
            {
                if(!state.Active)return;
                e=Create(view.GroundEntrySoilMaterial,view.gameObject.layer);
                e.Root.transform.position=view.transform.position-forward.normalized*SurvivorGroundEntryRules.CrawlDistance(state.Age);
                e.Root.transform.rotation=Quaternion.FromToRotation(Vector3.up,up.normalized);
                entries.Add(id,e);
            }
            if(!float.IsPositiveInfinity(e.StopAge))return;
            e.Age=Mathf.Max(e.Age,state.Age);e.Tail=!state.Active;Sample(e);
        }
        /// <summary>死亡、直接删除均停止后续喷发；已扬起的土仍在原地落完。</summary>
        public void Interrupt(int id)
        {if(entries.TryGetValue(id,out var e)){e.StopAge=Mathf.Min(e.StopAge,e.Age);e.Tail=true;}}
        /// <summary>由既有控制器生命周期调用；暂停不消耗独立效果寿命。</summary>
        public void Tick(float seconds, bool paused)
        {
            if(paused||seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return;
            expired.Clear();
            foreach(var pair in entries)
            {
                var e=pair.Value;
                if(e.Root==null){expired.Add(pair.Key);continue;}
                if(e.Tail)e.Age+=seconds;
                if(e.Age>=7){expired.Add(pair.Key);continue;}Sample(e);
            }
            foreach(var id in expired){Release(entries[id]);entries.Remove(id);}
        }
        /// <summary>从已预载材质建立独立土块和尘团，实例材质归效果所有。</summary>
        private Entry Create(Material source,int layer)
        {
            var e=new Entry{Root=new GameObject("GroundEntrySoil"),Clods=new Transform[28],Dust=new Transform[18],DustRenderers=new Renderer[18]};
            e.Root.transform.SetParent(parent,false);e.Root.layer=layer;
            e.Earth=new Material(source){color=new Color(.47f,.37f,.20f)};
            e.Dark=new Material(source){color=new Color(.35f,.24f,.13f)};
            e.DustMaterial=new Material(source){color=new Color(.53f,.45f,.30f,1)};
            e.DustMaterial.SetFloat("_Surface",1);e.DustMaterial.SetFloat("_SrcBlend",5);e.DustMaterial.SetFloat("_DstBlend",10);
            e.DustMaterial.SetFloat("_ZWrite",0);e.DustMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");e.DustMaterial.renderQueue=3000;
            e.Mark=Part(e.Root.transform,"DisturbedSoil",e.Earth,layer);e.Mark.localPosition=new Vector3(0,.03f,0);e.Mark.localScale=new Vector3(.8f,.06f,.7f);
            for(int i=0;i<28;i++)e.Clods[i]=Part(e.Root.transform,"Clod",i%3==0?e.Dark:e.Earth,layer);
            for(int i=0;i<18;i++){e.Dust[i]=Part(e.Root.transform,"Dust",e.DustMaterial,layer);(e.DustRenderers[i]=e.Dust[i].GetComponent<Renderer>()).shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
            return e;
        }
        /// <summary>创建无业务脚本的网格节点，统一由地图控制器推进。</summary>
        private Transform Part(Transform root,string name,Material material,int layer)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.layer=layer;go.transform.SetParent(root,false);
            go.GetComponent<MeshFilter>().sharedMesh=shape;go.GetComponent<MeshRenderer>().sharedMaterial=material;return go.transform;
        }
        private readonly MaterialPropertyBlock dustBlock=new(); // 尘团透明度只写独立覆盖块。
        /// <summary>逐项复现已采用预览的发射时刻、重力轨迹、淡出与随机角分布。</summary>
        private void Sample(Entry e)
        {
            float time=e.Age;
            for(int i=0;i<28;i++)
            {
                float angle=i*2.39996f,launch=.85f+(i%3)*.87f+(i%7)*.065f,elapsed=time-launch;
                bool emitted=elapsed>=0&&launch<e.StopAge;
                float speed=.4f+(i%5)*.13f,up=1+(i%4)*.28f,t=Mathf.Clamp(elapsed,0,2*up/9.81f);
                float radius=.3f+(i%4)*.095f+(emitted?speed*t:0),height=emitted?Mathf.Max(0,up*t-4.905f*t*t):0;
                var part=e.Clods[i];part.localPosition=new Vector3(Mathf.Cos(angle)*radius,.045f+height,Mathf.Sin(angle)*radius);
                part.localRotation=Quaternion.Euler(emitted?t*(90+i*11):0,i*47,emitted?t*(40+i*9):0);
                float fade=1-Mathf.SmoothStep(0,1,(time-5.8f-(i%5)*.08f)/.7f);
                part.localScale=new Vector3(.07f+(i%4)*.045f,.055f+(i%3)*.035f,.09f+(i%5)*.025f)*fade;
            }
            for(int i=0;i<18;i++)
            {
                float launch=.9f+(i%3)*.87f,dt=time-launch,progress=Mathf.Clamp01(dt/2.3f),angle=i*2.39996f;
                var part=e.Dust[i];part.gameObject.SetActive(dt>=0&&dt<2.3f&&launch<e.StopAge);
                float r=.35f+progress*.8f;part.localPosition=new Vector3(Mathf.Cos(angle)*r,.06f+progress*.1f,Mathf.Sin(angle)*r);
                part.localScale=new Vector3(.18f+progress*.38f,.08f+progress*.10f,.15f+progress*.3f);
                var color=new Color(.53f,.45f,.30f,.24f*Mathf.Sin(progress*Mathf.PI));dustBlock.SetColor("_BaseColor",color);dustBlock.SetColor("_Color",color);e.DustRenderers[i].SetPropertyBlock(dustBlock);
            }
        }
        /// <summary>清场释放所有独立效果，保留地图共享形状供后续使用。</summary>
        public void Clear(){foreach(var e in entries.Values)Release(e);entries.Clear();}
        /// <summary>离图先释放效果与材质，再释放共享网格；允许重复清理。</summary>
        public void Dispose(){Clear();Destroy(shape);}
        /// <summary>释放单个效果持有的实例资源，不销毁预载Prefab材质。</summary>
        private static void Release(Entry e){Destroy(e.Root);Destroy(e.Earth);Destroy(e.Dark);Destroy(e.DustMaterial);}
        /// <summary>按运行环境选择安全释放方式。</summary>
        private static void Destroy(Object value){if(value==null)return;if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
        /// <summary>采用版低多边形土块，展开面顶点保持硬边法线。</summary>
        private static Vector3[] CreateGroundClodShape()
        {
            float t=(1+Mathf.Sqrt(5))/2;var v=new[]{new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1)};
            int[] triangles={0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};var result=new Vector3[triangles.Length];for(int i=0;i<result.Length;i++)result[i]=v[triangles[i]].normalized;return result;
        }
    }
}
