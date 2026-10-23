using System;
using UnityEngine;

namespace Game
{
    /// <summary>已采用浓紫雾的只读采样器；实例由地图持有，所有动画只取权威活动时钟。</summary>
    internal sealed class ArcadeVortexBarrelPresentation
    {
        private readonly ParticleSystem smoke; // 正式浓雾实例，不拥有共享材质。
        private readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[190]; // 复用已采用候选的有界粒子预算。
        private readonly LineRenderer[] ribbons; // 两条向桶口收拢的漩涡线。
        private double sampledAt=double.NaN; // 重复权威时刻无需再次写入粒子。
        /// <summary>采用预览中实测主角高度，纯外观尺寸，不参与吸物或伤害判定。</summary>
        private const float OuterRadius=1.761965f;
        /// <summary>校验正式模板并初始化停止态粒子缓冲，不推进自主计时。</summary>
        public ArcadeVortexBarrelPresentation(Transform root)
        {
            smoke=root.GetComponentInChildren<ParticleSystem>(true);
            ribbons=Array.FindAll(root.GetComponentsInChildren<LineRenderer>(true),line=>line.name=="ConvergingRibbon");
            if(smoke==null||ribbons.Length!=2)throw new InvalidOperationException("正式紫桶缺少已采用的漩涡模板");
            // 停止态Prefab首次实例化还没有粒子缓冲；零时长初始化后再写入，不推进动画时钟。
            smoke.Simulate(0,true,true,true);smoke.Pause();
        }
        /// <summary>确定性重采样已采用候选的浓雾轨迹；暂停时保持同一画面。</summary>
        public void Sample(double elapsed)
        {
            if(smoke==null||elapsed==sampledAt||double.IsNaN(elapsed)||double.IsInfinity(elapsed)||elapsed<0)return;
            sampledAt=elapsed;float time=(float)elapsed;
            for(int arm=0;arm<ribbons.Length;arm++)for(int step=0;step<72;step++)
            {
                float u=step/71f,r=.09f+(OuterRadius-.09f)*u,angle=u*Mathf.PI*2.5f-time*3.5f+arm*Mathf.PI;
                ribbons[arm].SetPosition(step,new Vector3(Mathf.Cos(angle)*r,.05f+u*.5f,Mathf.Sin(angle)*r));
            }
            for(int i=0;i<particles.Length;i++)
            {
                float age=Mathf.Repeat(time*.34f+(i/4)/47.5f,1),u=1-age;
                float r=(.50f+(OuterRadius*1.5f-.50f)*u)*(.97f+.03f*Mathf.Sin(i*4.1f)),angle=(i%4)*Mathf.PI*.5f-time*2.3f+u*Mathf.PI*2.5f;
                particles[i].position=new Vector3(Mathf.Cos(angle)*r,.06f+u*.55f,Mathf.Sin(angle)*r);
                particles[i].startSize=.28f+.6f*u;particles[i].startColor=new Color(.70f,.055f,.98f,.45f*Mathf.Pow(Mathf.Sin(age*Mathf.PI),.65f));
                particles[i].startLifetime=10;particles[i].remainingLifetime=10;particles[i].rotation=i*41+time*30;
            }
            smoke.SetParticles(particles,particles.Length);smoke.Pause();
        }
    }
}
