using System;
using UnityEngine;

namespace Game
{
    /// <summary>只投影水面摆臂与身体姿态，不移动角色根、碰撞或决定游泳；句柄及恢复快照归当前PlayerView。</summary>
    internal sealed class PlayerSwimmingPresenter : IDisposable
    {
        private readonly Animator animator;
        private static readonly string[] MuscleNames=HumanTrait.MuscleName;
        private HumanPoseHandler handler;
        private Avatar avatar;
        private HumanPose pose, basePose;
        private AnimatorCullingMode originalCulling;
        private bool active, captured;
        private float phase;
        public PlayerSwimmingPresenter(Animator animator){this.animator=animator;}
        internal void SetActive(bool value)
        {
            if(active==value)return;
            if(!value)
            {
                if(animator!=null){if(captured && handler!=null)handler.SetHumanPose(ref basePose);animator.cullingMode=originalCulling;}
                captured=false;active=false;phase=0;return;
            }
            active=true;
            if(animator!=null){originalCulling=animator.cullingMode;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;}
        }
        /// <summary>在Animator求值后的LateUpdate调用；暂停保持姿态，换Avatar重建句柄，退出恢复最近的动画基础姿态。</summary>
        internal void Apply(float deltaSeconds)
        {
            if(!active||deltaSeconds<=0||animator==null||!animator.isHuman)return;
            if(avatar!=animator.avatar)
            {
                handler?.Dispose();avatar=animator.avatar;handler=new HumanPoseHandler(avatar,animator.transform);
                pose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};basePose=new HumanPose{muscles=new float[HumanTrait.MuscleCount]};captured=false;
            }
            handler.GetHumanPose(ref pose);
            basePose.bodyPosition=pose.bodyPosition;basePose.bodyRotation=pose.bodyRotation;
            Array.Copy(pose.muscles,basePose.muscles,pose.muscles.Length);captured=true;
            phase=(phase+deltaSeconds*2.4f)%(Mathf.PI*2);
            pose.bodyRotation*=Quaternion.Euler(65,0,0);
            void Muscle(string name,float value){var index=Array.IndexOf(MuscleNames,name);if(index>=0)pose.muscles[index]=value;}
            var stroke=Mathf.Sin(phase);
            Muscle("Left Arm Front-Back",.35f+.35f*stroke);Muscle("Right Arm Front-Back",.35f-.35f*stroke);
            Muscle("Left Arm Down-Up",-.15f);Muscle("Right Arm Down-Up",-.15f);
            Muscle("Left Forearm Stretch",.3f);Muscle("Right Forearm Stretch",.3f);
            Muscle("Left Upper Leg Front-Back",.12f*stroke);Muscle("Right Upper Leg Front-Back",-.12f*stroke);
            handler.SetHumanPose(ref pose);
        }
        public void Dispose(){SetActive(false);handler?.Dispose();handler=null;avatar=null;}
    }
}
