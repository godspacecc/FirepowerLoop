using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>仅本地图拥有的命中电弧，独立于危险领域，不请求异步资产或创建碰撞体。</summary>
        private readonly Dictionary<int,LineRenderer> purpleRayArcs=new();
        private GameObject purpleRayArcRoot;

        private void SetPurpleRayArcs(double elapsed,IReadOnlyList<ArcadeContentAreaSnapshot> areas)
        {
            if(root==null){ClearPurpleRayArcs();return;}
            if(purpleRayArcRoot!=null&&purpleRayArcRoot.transform.parent!=root.transform)ClearPurpleRayArcs();
            var active=new HashSet<int>();
            foreach(var arc in areas??Array.Empty<ArcadeContentAreaSnapshot>())
            {
                if(arc.Kind!=ArcadeRayArcState.Kind||!arc.Segment||arc.Warning||!FiniteArc(arc.X)||!FiniteArc(arc.Y)||!FiniteArc(arc.EndX)||!FiniteArc(arc.EndY)||!FiniteArc(arc.Radius)||arc.Radius<=0)continue;
                active.Add(arc.Id);
                if(purpleRayArcRoot==null)purpleRayArcRoot=RoundGroup(root.transform,"PurpleRayHitArcs");
                if(!purpleRayArcs.TryGetValue(arc.Id,out var line))
                {
                    line=RoundGroup(purpleRayArcRoot.transform,"PurpleRayArc_"+arc.Id).AddComponent<LineRenderer>();
                    var shader=Shader.Find("Universal Render Pipeline/Unlit");
                    if(shader==null)throw new InvalidOperationException("紫射线电弧缺少正式Unlit Shader");
                    var material=new Material(shader){name="RoundOwnedPurpleRayArc",color=new Color(.8f,.3f,1)};
                    if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",new Color(.8f,.3f,1));
                    roundOwnedMaterials.Add(material);line.sharedMaterial=material;
                    line.useWorldSpace=false;line.positionCount=13;line.widthMultiplier=.055f;purpleRayArcs.Add(arc.Id,line);
                }
                var from=new Vector3(arc.X,.9f,arc.Y);var to=new Vector3(arc.EndX,.9f,arc.EndY);
                float phase=double.IsNaN(elapsed)||double.IsInfinity(elapsed)?0:(float)(elapsed%1000)*13.5f;
                for(int i=0;i<13;i++)
                {
                    var at=Vector3.Lerp(from,to,i/12f);if(i>0&&i<12)at.x+=Mathf.Sin(i*7+phase)*.19f;
                    line.SetPosition(i,at);
                }
            }
            foreach(int id in purpleRayArcs.Keys.Where(id=>!active.Contains(id)).ToArray())RemovePurpleRayArc(id);
        }
        private static bool FiniteArc(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private void RemovePurpleRayArc(int id)
        {
            var line=purpleRayArcs[id];if(line!=null){var material=line.sharedMaterial;if(roundOwnedMaterials.Remove(material))DestroyObject(material);line.gameObject.SetActive(false);DestroyObject(line.gameObject);}purpleRayArcs.Remove(id);
        }
        /// <summary>先移除可见对象再清拥有关系；切轮、End及根更换均不可恢复旧电弧。</summary>
        private void ClearPurpleRayArcs()
        {
            foreach(int id in purpleRayArcs.Keys.ToArray())RemovePurpleRayArc(id);
            if(purpleRayArcRoot!=null)purpleRayArcRoot.SetActive(false);DestroyObject(purpleRayArcRoot);purpleRayArcRoot=null;
        }
    }
}
