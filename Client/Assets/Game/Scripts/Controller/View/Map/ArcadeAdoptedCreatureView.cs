using System;
using UnityEngine;

namespace Game
{
    /// <summary>四种已采用演员的持久片段容器；只采样局部姿态，不推进时钟或战斗。</summary>
    public sealed class ArcadeAdoptedCreatureView : MonoBehaviour
    {
        /// <summary>与网格材质一起由既有Content聚合加载事务持有。</summary>
        [SerializeField] private AnimationClip[] poses=Array.Empty<AnimationClip>();
        private static readonly string[] Required={"Idle","Move","Windup","Action","Recovery","Rush"};
        /// <summary>从正式待机Prefab读取连接基准；只缓存本演员节点，不持有额外资源。</summary>
        private bool rigCached;
        private Transform core;
        private Vector3[] shoulderAnchors;
        private Transform[] armParts;
        public bool IsReady=>poses!=null&&Array.TrueForAll(Required,n=>Array.Exists(poses,c=>c!=null&&c.name==n));
        /// <summary>绝对时间采样；片段不能包含演员根曲线。</summary>
        public void SamplePose(string pose,float seconds)
        {if(float.IsNaN(seconds)||float.IsInfinity(seconds))return;var clip=Find(pose);Sample(clip,Mathf.Clamp(seconds,0,clip.length));}
        /// <summary>把原权威窗口进度映射到采用版片段，保留原规则时长。</summary>
        public void SampleProgress(string pose,float progress)
        {if(float.IsNaN(progress)||float.IsInfinity(progress))return;var clip=Find(pose);Sample(clip,Mathf.Clamp01(progress)*clip.length);}
        /// <summary>只平移每侧完整手臂到正式肩锚，消除步态髋起伏及片段插值间隙；不改骨角或骨长。</summary>
        private void Sample(AnimationClip clip,float seconds)
        {
            if(!rigCached)
            {
                rigCached=true;core=transform.Find("Body/Core");
                if(core!=null)
                {
                    armParts=new Transform[8];shoulderAnchors=new Vector3[2];int i=0;
                    foreach(string side in new[]{"L","R"})foreach(string part in new[]{"Upper","Lower","Elbow","Hand"})armParts[i++]=transform.Find("Body/Arm"+side+"/"+part);
                    for(int side=0;side<2;side++)shoulderAnchors[side]=Quaternion.Inverse(core.localRotation)*(ArmStart(armParts[side*4])-core.localPosition);
                }
            }
            clip.SampleAnimation(gameObject,seconds);
            if(core==null)return;
            for(int side=0;side<2;side++)
            {
                var offset=core.localPosition+core.localRotation*shoulderAnchors[side]-ArmStart(armParts[side*4]);
                for(int part=0;part<4;part++)armParts[side*4+part].localPosition+=offset;
            }
        }
        private static Vector3 ArmStart(Transform upper)=>upper.localPosition-upper.localRotation*Vector3.up*upper.localScale.y*.5f;
        private AnimationClip Find(string pose)
        {foreach(var clip in poses)if(clip!=null&&clip.name==pose)return clip;throw new InvalidOperationException("采用版角色缺少正式片段："+pose);}
    }
}
