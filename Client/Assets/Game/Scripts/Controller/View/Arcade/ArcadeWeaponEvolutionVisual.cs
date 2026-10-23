using UnityEngine;
namespace Game
{
    /// <summary>已采用紫级特效的纯表现时钟，由街机现有表现帧推进，不裁决伤害或自行Update。</summary>
    public sealed class ArcadeWeaponEvolutionVisual:MonoBehaviour
    {
        /// <summary>1残火、2宽束、3榴弹体、4主爆、5子爆，与正式模板一一对应。</summary>
        [SerializeField]private int mode;
        private ParticleSystem[] particles;
        private float[] rates;
        private LineRenderer[] lines;
        private float remaining=2,phase;
        private bool child;
        public float ElapsedSeconds{get;private set;}
        private void Ensure()
        {
            if(particles!=null)return;
            particles=GetComponentsInChildren<ParticleSystem>(true);lines=GetComponentsInChildren<LineRenderer>(true);rates=new float[particles.Length];
            for(int i=0;i<particles.Length;i++){rates[i]=particles[i].emission.rateOverTime.constant;particles[i].Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);particles[i].Play(false);particles[i].Pause(false);}
        }
        /// <summary>只消费当前权威剩余量和抛射进度，重发快照不重启粒子或蓄能。</summary>
        public void Apply(float radius,float seconds,bool isChild,float travelPhase)
        {
            Ensure();remaining=Mathf.Max(0,seconds);child=isChild;phase=Mathf.Clamp01(travelPhase);
            if(mode==1||mode==2)transform.localScale=Vector3.one*Mathf.Max(.01f,radius/.65f);
            if(mode==3)
            {
                foreach(var p in particles)if(p.name.Contains("trail")){var main=p.main;float size=child?.18f:.3f;main.startSize=new ParticleSystem.MinMaxCurve(size*.65f,size);}
                foreach(var mesh in GetComponentsInChildren<MeshRenderer>(true))
                {mesh.transform.localScale=child?new Vector3(.3f,.23f,.3f):new Vector3(.42f,.3f,.42f);mesh.transform.localRotation=Quaternion.Euler(phase*540,0,35);}
            }
        }
        /// <summary>零步长保持完整粒子状态；暂停、卸载及异步过期均由外层生命周期决定。</summary>
        public void Tick(float seconds)
        {
            if(seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return;
            Ensure();ElapsedSeconds+=seconds;
            for(int i=0;i<particles.Length;i++)
            {
                if(mode==1){var emission=particles[i].emission;emission.rateOverTime=rates[i]*Mathf.Clamp01(remaining/.65f);}
                particles[i].Simulate(seconds,false,false,true);
            }
            if(mode==2)
            {
                foreach(var line in lines)
                {
                    if(!line.name.Contains("flow"))continue;int index=line.name.EndsWith("1")?1:0;
                    for(int j=0;j<18;j++){float f=j/17f;line.SetPosition(j,new Vector3(Mathf.Sin(f*16-ElapsedSeconds*35+index*Mathf.PI)*.65f*.65f,0,Mathf.Lerp(-2.5f,0,f)));}
                }
            }
            if(mode==4||mode==5)
            {
                float scale=mode==5?1.2f:1.65f,age=ElapsedSeconds;
                foreach(var line in lines)
                {
                    line.enabled=age<.45f;if(!line.enabled)continue;
                    line.startWidth=line.endWidth=.09f*(1-age/.45f);line.startColor=line.endColor=new Color(.8f,.35f,1,(1-age/.45f)*.6f);
                    for(int i=0;i<65;i++){float a=i*Mathf.PI/32;line.SetPosition(i,new Vector3(Mathf.Cos(a)*(.2f+age*6)*scale,-.43f,Mathf.Sin(a)*(.2f+age*6)*scale));}
                }
            }
        }
        private void OnDisable(){if(particles!=null)foreach(var p in particles)p.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);}
    }
}
