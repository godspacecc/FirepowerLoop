using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>地图持有的角色冲刺表现；只读剩余时间，不推断速度或修改战斗状态。</summary>
    internal sealed class ArcadeDashPresentation : IDisposable
    {
        // 仅控制视觉分段和丢包后的自动收尾，实际冲刺时长由规则层决定。
        private const float VisualDashSeconds=.28f, TailSeconds=.17f;
        private readonly Transform parent, template;
        private readonly Dictionary<long, Entry> entries=new();
        private static readonly int BaseColor=Shader.PropertyToID("_BaseColor");
        /// <summary>每个可见玩家独占一个可复用槽位，起步方向在本次表现内保持不变。</summary>
        private sealed class Entry
        {
            public GameObject Root;
            public Transform Burst, Ribbons, Fragments, Dust;
            public Renderer[] Renderers;
            public Color[] Colors;
            public readonly MaterialPropertyBlock Block=new();
            public float Age, Remaining, ObservedRemaining;
            public bool ObservedActive;
        }
        public ArcadeDashPresentation(Transform parent,Transform template)
        {this.parent=parent;this.template=template;}
        /// <summary>由单人状态或联机权威快照更新；重复快照不重新播放起步。</summary>
        public void Set(long id,Vector2 position,Vector2 direction,float remaining,bool visible)
        {
            if(parent==null||template==null)return;
            entries.TryGetValue(id,out var entry);
            if(!visible){if(entry!=null){entry.Root.SetActive(false);entry.ObservedActive=false;entry.ObservedRemaining=0;}return;}
            bool active=remaining>0;
            if(entry==null)
            {
                if(!active)return;
                var instance=UnityEngine.Object.Instantiate(template.gameObject,parent,false);
                instance.name="Dash_"+id;
                entry=new Entry{Root=instance,Burst=instance.transform.Find("Burst"),Ribbons=instance.transform.Find("Ribbons"),Fragments=instance.transform.Find("Fragments"),Dust=instance.transform.Find("Dust")};
                entry.Renderers=instance.GetComponentsInChildren<Renderer>(true);entry.Colors=new Color[entry.Renderers.Length];
                for(int i=0;i<entry.Renderers.Length;i++)entry.Colors[i]=entry.Renderers[i].sharedMaterial.GetColor(BaseColor);
                entries.Add(id,entry);
            }
            bool starts=active&&(!entry.ObservedActive||remaining>entry.ObservedRemaining+.04f);
            if(starts)
            {
                entry.Age=Mathf.Max(0,VisualDashSeconds-remaining);
                entry.Remaining=Mathf.Min(VisualDashSeconds,remaining);
                var forward=new Vector3(direction.x,0,direction.y);
                entry.Root.transform.localRotation=Quaternion.LookRotation(forward.sqrMagnitude>.001f?forward.normalized:Vector3.forward);
                entry.Root.SetActive(true);
            }
            else if(!active&&entry.ObservedActive)
            {entry.Age=Mathf.Max(entry.Age,VisualDashSeconds);entry.Remaining=0;}
            entry.ObservedActive=active;entry.ObservedRemaining=remaining;
            Move(id,position,visible);
            Render(entry);
        }
        /// <summary>本机预测只移动正在冲刺的视觉锚点，不用预测位置触发新冲刺。</summary>
        public void Move(long id,Vector2 position,bool visible)
        {
            if(!entries.TryGetValue(id,out var entry))return;
            if(!visible){entry.Root.SetActive(false);entry.ObservedActive=false;return;}
            if(entry.Remaining>0&&entry.Root.activeSelf)entry.Root.transform.localPosition=new Vector3(position.x,.12f,position.y);
        }
        /// <summary>所有动画由既有生命周期推进；暂停零步长冻结，缺失快照也会自动结束。</summary>
        public void Tick(float seconds)
        {
            if(seconds<=0)return;
            foreach(var entry in entries.Values)
            {
                if(!entry.Root.activeSelf)continue;
                entry.Age+=seconds;entry.Remaining=Mathf.Max(0,entry.Remaining-seconds);
                Render(entry);
            }
        }
        private static void Render(Entry e)
        {
            if(e.Age>=VisualDashSeconds+TailSeconds){e.Root.SetActive(false);return;}
            float tail=1-Mathf.Clamp01((e.Age-VisualDashSeconds)/TailSeconds);
            e.Burst.gameObject.SetActive(e.Age<.085f);
            e.Burst.localScale=Vector3.one*Mathf.Lerp(.6f,1.3f,Mathf.Clamp01(e.Age/.085f));
            e.Ribbons.localScale=new Vector3(1,1,Mathf.Lerp(.18f,1,Mathf.Clamp01(e.Age/.07f)));
            e.Ribbons.gameObject.SetActive(e.Age<VisualDashSeconds);
            e.Fragments.gameObject.SetActive(e.Age>=VisualDashSeconds);
            e.Fragments.localScale=new Vector3(1,1,Mathf.Lerp(.45f,1,tail));
            e.Fragments.localPosition=Vector3.back*(1.1f+(1-tail)*.4f);
            e.Dust.localPosition=Vector3.back*(.1f+e.Age*1.7f);
            e.Dust.localScale=Vector3.one*(.65f+e.Age*1.4f);
            for(int i=0;i<e.Renderers.Length;i++)
            {
                var renderer=e.Renderers[i];var color=e.Colors[i];
                float alpha=renderer.transform==e.Burst?1-Mathf.Clamp01(e.Age/.085f):tail;
                color.a*=alpha;e.Block.SetColor(BaseColor,color);renderer.SetPropertyBlock(e.Block);
            }
        }
        /// <summary>离开可见区域或断线时释放该玩家，不能留下悬空尾迹。</summary>
        public void Remove(long id)
        {
            if(!entries.TryGetValue(id,out var entry))return;
            entries.Remove(id);Destroy(entry.Root);
        }
        private static void Destroy(GameObject go)
        {if(go==null)return;go.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(go);else UnityEngine.Object.DestroyImmediate(go);}
        public void Dispose(){foreach(var entry in entries.Values)Destroy(entry.Root);entries.Clear();}
    }
}
