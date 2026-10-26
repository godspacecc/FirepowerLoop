using System;
using UnityEngine;

namespace Game
{
    /// <summary>正式小龙的持久关节片段；只采样，不更新战斗时间或移动演员根。</summary>
    public sealed class ArcadeFinalDragonView : MonoBehaviour
    {
        [SerializeField] private AnimationClip[] poses=Array.Empty<AnimationClip>();
        private static readonly string[] Required={"Idle","Move","Windup","Charge","Roar","Wings","RoarUpper","WingsUpper"};
        public bool IsReady=>poses!=null&&Array.TrueForAll(Required,name=>Array.Exists(poses,c=>c!=null&&c.name==name));
        /// <summary>直接采样绝对片段时间，暂停和重复快照不会自行推进。</summary>
        public void SamplePose(string pose,float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds))return;
            foreach(var clip in poses)if(clip!=null&&clip.name==pose){clip.SampleAnimation(gameObject,Mathf.Clamp(seconds,0,clip.length));return;}
            throw new InvalidOperationException("终局小龙缺少正式片段："+pose);
        }
    }
}
