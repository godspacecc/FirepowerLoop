using System;
using UnityEngine;
namespace Game
{
    /// <summary>幸存者短爆燃只采样权威进度；不自行计时，不参与伤害和碰撞。</summary>
    public sealed class SurvivorBlastView : MonoBehaviour
    {
        /// <summary>采用版闪光、爆燃和短烟，由正式Prefab显式绑定。</summary>
        [SerializeField] private ParticleSystem[] particles=Array.Empty<ParticleSystem>();
        /// <summary>与现有领域爆炸寿命一致，仅用于把归一化表现进度换算为秒。</summary>
        private const float PresentationSeconds=.4f;
        /// <summary>本次租用已采样时间；反向采样或回池后从零重新开始。</summary>
        private float sampledSeconds;
        private bool sampled;
        /// <summary>启用和回收均清空粒子，世界空间烟雾不能跨位置或地图残留。</summary>
        private void OnEnable()=>ResetForReuse();
        private void OnDisable()=>ResetForReuse();
        /// <summary>同进度不推进；只有权威战斗时间变化才模拟增量，粒子始终暂停自动时钟。</summary>
        public void Apply(Vector3 position,float radius,float progress)
        {
            if(radius<=0||float.IsNaN(radius)||float.IsInfinity(radius)||float.IsNaN(progress)||float.IsInfinity(progress))return;
            transform.position=position+Vector3.up*.75f;
            transform.localScale=Vector3.one*Mathf.Clamp(radius/2.8f,.35f,2f);
            float target=Mathf.Clamp01(progress)*PresentationSeconds;
            bool restart=!sampled||target<sampledSeconds;
            if(!restart&&target==sampledSeconds)return;
            float delta=restart?target:target-sampledSeconds;
            foreach(var system in particles)
            {
                system.Simulate(delta,false,restart,false);
                system.Pause(false);
            }
            sampled=true;sampledSeconds=target;
        }
        /// <summary>沿既有投射物预加载事务校验真实粒子及显式材质，缺失时拒绝开局。</summary>
        public bool Validate(out string error)
        {
            error=null;
            if(particles==null||particles.Length!=3){error="幸存者短爆燃需要闪光、爆燃及短烟三项";return false;}
            foreach(var system in particles)
            {
                if(system==null||!system.transform.IsChildOf(transform)){error="幸存者爆燃粒子引用缺失或不属于模板";return false;}
                var renderer=system.GetComponent<ParticleSystemRenderer>();
                if(system.main.loop||system.main.playOnAwake||renderer==null||renderer.sharedMaterial==null||renderer.sharedMaterial.shader==null){error="幸存者爆燃自动时钟或材质引用无效";return false;}
            }
            return true;
        }
        /// <summary>回池只清理表现状态，不修改材质、规则或共享资源。</summary>
        public void ResetForReuse()
        {
            sampled=false;sampledSeconds=0;
            if(particles==null)return;
            foreach(var system in particles)if(system!=null)system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
