using System;
using UnityEngine;

namespace Game
{
    /// <summary>采用版四足演员只负责正式动画采样；没有Update、位移积分或战斗判定。</summary>
    public sealed class ArcadeCreatureView : MonoBehaviour
    {
        /// <summary>由采用版烘焙器绑定的持久动画，和网格材质一起由既有模板加载事务拥有。</summary>
        [SerializeField] private AnimationClip[] poses = Array.Empty<AnimationClip>();
        public bool IsReady => poses != null && poses.Length >= 9 && Array.TrueForAll(poses,p=>p!=null);

        /// <summary>绝对时间直接采样全部动态节点，片段不包含演员根位置。</summary>
        public void SamplePose(string pose,float seconds)
        {
            if(float.IsNaN(seconds)||float.IsInfinity(seconds))return;
            foreach(var clip in poses)
                if(clip!=null&&clip.name==pose)
                {clip.SampleAnimation(gameObject,Mathf.Clamp(seconds,0,clip.length));return;}
            throw new InvalidOperationException("四足演员缺少正式片段："+pose);
        }
    }
}
