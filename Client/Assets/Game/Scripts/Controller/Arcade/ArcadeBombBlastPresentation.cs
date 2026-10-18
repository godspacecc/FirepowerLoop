using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>地图持有的炸弹三阶段表现池；只缩放、渐隐和回收，不参与伤害或输入。</summary>
    internal sealed class ArcadeBombBlastPresentation : IDisposable
    {
        /// <summary>最多同时保留四组；连续使用复用最旧槽位。</summary>
        private const int Capacity=4;
        /// <summary>模板借自当前地图资源句柄，实例由本池独占。</summary>
        private readonly Transform parent,template;
        private readonly List<Entry> entries=new();
        private int cursor;
        private static readonly int BaseColor=Shader.PropertyToID("_BaseColor");
        /// <summary>一组爆心、冲击波与颗粒的缓存；避免每帧查询组件和创建材质。</summary>
        private sealed class Entry
        {
            public GameObject Root;
            public Transform Flash;
            public MeshRenderer FlashRenderer;
            public LineRenderer Ring;
            public LineRenderer Halo;
            public MeshRenderer Rays;
            public Color RayColor;
            public MeshRenderer Glow;
            public Color GlowColor;
            public ParticleSystem[] Particles;
            public readonly MaterialPropertyBlock Block=new();
            public Color FlashColor;
            public float Age,Radius;
        }
        public ArcadeBombBlastPresentation(Transform parent,Transform template)
        {this.parent=parent;this.template=template;}
        /// <summary>固定爆心，不跟随角色；半径来自场地几何，仅用于覆盖可见地面。</summary>
        public void Play(Vector2 position,float radius)
        {
            if(parent==null||template==null)return;
            Entry entry;
            if(entries.Count<Capacity)
            {
                var root=UnityEngine.Object.Instantiate(template.gameObject,parent,false);root.name="BombBlastInstance";
                var flash=root.transform.Find("Flash");var renderer=flash.GetComponent<MeshRenderer>();
                entry=new Entry{Root=root,Flash=flash,FlashRenderer=renderer,Ring=root.transform.Find("Shockwave").GetComponent<LineRenderer>(),
                    Particles=root.GetComponentsInChildren<ParticleSystem>(true),FlashColor=renderer.sharedMaterial.GetColor(BaseColor)};
                entry.Halo=root.transform.Find("ShockwaveHalo").GetComponent<LineRenderer>();
                entry.Rays=root.transform.Find("Rays").GetComponent<MeshRenderer>();entry.RayColor=entry.Rays.sharedMaterial.GetColor(BaseColor);
                entry.Glow=root.transform.Find("Glow").GetComponent<MeshRenderer>();entry.GlowColor=entry.Glow.sharedMaterial.GetColor(BaseColor);
                entries.Add(entry);
            }
            else entry=entries[cursor];
            cursor=(cursor+1)%Capacity;
            foreach(var ps in entry.Particles)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            entry.Root.transform.localPosition=new Vector3(position.x,.18f,position.y);entry.Root.SetActive(true);
            // 最大表现半径覆盖常用俯视镜头，避免大地图把扩散过程压缩成一帧；清场范围不由此值决定。
            entry.Age=0;entry.Radius=Mathf.Clamp(radius,4,14);Render(entry);
            foreach(var ps in entry.Particles)ps.Play(false);
        }
        /// <summary>由已有战斗生命周期推进，暂停时调用者不推进。</summary>
        public void Tick(float seconds)
        {
            if(seconds<=0)return;
            foreach(var entry in entries)
            {
                if(!entry.Root.activeSelf)continue;
                entry.Age+=seconds;
                if(entry.Age>=1f){foreach(var ps in entry.Particles)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);entry.Root.SetActive(false);continue;}
                Render(entry);
            }
        }
        private static void Render(Entry entry)
        {
            float age=entry.Age;
            entry.Flash.gameObject.SetActive(age<.17f);
            entry.Flash.localScale=Vector3.one*Mathf.Lerp(2.4f,5f,Mathf.Clamp01(age/.17f));
            var color=entry.FlashColor;color.a*=1-Mathf.Clamp01(age/.17f);entry.Block.SetColor(BaseColor,color);entry.FlashRenderer.SetPropertyBlock(entry.Block);
            entry.Ring.gameObject.SetActive(age<.5f);
            float progress=Mathf.Clamp01(age/.45f);float radius=Mathf.Lerp(.6f,entry.Radius,progress);
            entry.Ring.transform.localScale=Vector3.one*radius;
            entry.Ring.widthMultiplier=Mathf.Lerp(.36f,.20f,progress);
            float alpha=(1-Mathf.Clamp01((age-.23f)/.27f))*Mathf.Clamp01(age/.025f);
            entry.Ring.startColor=entry.Ring.endColor=new Color(1,1,1,alpha);
            entry.Halo.gameObject.SetActive(age<.5f);entry.Halo.transform.localScale=entry.Ring.transform.localScale;
            entry.Halo.startColor=entry.Halo.endColor=new Color(1,1,1,alpha*.16f);
            entry.Rays.gameObject.SetActive(age<.65f);
            entry.Rays.transform.localScale=Vector3.one*Mathf.Lerp(2,Mathf.Min(entry.Radius,12),Mathf.Clamp01(age/.55f));
            color=entry.RayColor;color.a*=1-Mathf.Clamp01((age-.12f)/.53f);entry.Block.SetColor(BaseColor,color);entry.Rays.SetPropertyBlock(entry.Block);
            entry.Glow.gameObject.SetActive(age<.4f);entry.Glow.transform.localScale=Vector3.one*Mathf.Lerp(3,6,Mathf.Clamp01(age/.4f));
            color=entry.GlowColor;color.a*=1-Mathf.Clamp01(age/.4f);entry.Block.SetColor(BaseColor,color);entry.Glow.SetPropertyBlock(entry.Block);
        }
        /// <summary>先销毁实例，再由地图释放模板租约。</summary>
        public void Dispose()
        {
            foreach(var entry in entries)if(entry.Root!=null)
            {entry.Root.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(entry.Root);else UnityEngine.Object.DestroyImmediate(entry.Root);}
            entries.Clear();cursor=0;
        }
    }
}
