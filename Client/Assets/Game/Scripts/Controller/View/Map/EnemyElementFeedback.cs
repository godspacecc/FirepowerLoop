using System;
using UnityEngine;
namespace Game
{
    /// <summary>采用版元素粒子与冷色覆盖；只消费权威状态，由既有敌人表现帧推进。</summary>
    public sealed class EnemyElementFeedback : MonoBehaviour
    {
        [SerializeField] private ParticleSystem fireParticles;
        [SerializeField] private ParticleSystem frostParticles;
        [SerializeField] private SkinnedMeshRenderer[] frostRenderers=Array.Empty<SkinnedMeshRenderer>();
        /// <summary>实例级状态和可复用颜色槽，不改共享材质或生命／速度。</summary>
        private bool burning,frosted,coldApplied;
        private ColorBinding[] bindings;
        public bool IsBurning=>burning;
        public bool IsFrosted=>frosted;
        /// <summary>入场先验证依赖与身体引用，缺少正式效果时明确拒绝交接战场。</summary>
        public bool Validate(out string error)
        {
            error=null;
            if(fireParticles==null||frostParticles==null||frostRenderers==null||frostRenderers.Length==0||Array.Exists(frostRenderers,r=>r==null))
            {error="元素效果缺少粒子或身体引用";return false;}
            foreach(var particles in new[]{fireParticles,frostParticles})
            {
                var renderer=particles.GetComponent<ParticleSystemRenderer>();var material=renderer!=null?renderer.sharedMaterial:null;
                if(material==null||material.shader==null||material.GetTexture("_BaseMap")==null||particles.main.playOnAwake)
                {error="元素效果材质、纹理或手动时钟配置无效";return false;}
            }
            return true;
        }
        /// <summary>持续同步不重播；权威状态关闭时立即清除，避免到期残留。</summary>
        public void SetState(bool fire,bool frost)
        {
            if(burning!=fire){burning=fire;SetParticles(fireParticles,fire);}
            if(frosted==frost)return;
            frosted=frost;SetParticles(frostParticles,frost);
            if(frost){CaptureColors();ApplyCold();}else RestoreColors();
        }
        /// <summary>粒子始终处于手动暂停状态，零步长冻结；受击闪红时暂时归还冷色覆盖。</summary>
        public void Tick(float seconds,bool hitFlash)
        {
            if(seconds>0&&float.IsFinite(seconds))
            {if(burning)Advance(fireParticles,seconds);if(frosted)Advance(frostParticles,seconds);}
            if(frosted&&!hitFlash){if(!coldApplied)ApplyCold();}
            else if(coldApplied)RestoreColors();
        }
        /// <summary>死亡、回池、禁用和切图共用幂等归还路径。</summary>
        public void ResetForReuse()
        {burning=frosted=false;SetParticles(fireParticles,false);SetParticles(frostParticles,false);RestoreColors();}
        private static void SetParticles(ParticleSystem particles,bool active)
        {
            if(particles==null)return;
            if(!active){particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);return;}
            particles.Play(false);particles.Pause(false);
        }
        private static void Advance(ParticleSystem particles,float seconds)
        {if(particles==null)return;particles.Simulate(seconds,false,false,true);particles.Pause(false);}
        /// <summary>保存每个材质槽的原覆盖，材质本色分别混合，不能把红闪当成恢复基线。</summary>
        private void CaptureColors()
        {
            if(bindings==null)
            {bindings=new ColorBinding[frostRenderers.Length];for(int i=0;i<bindings.Length;i++)if(frostRenderers[i]!=null)bindings[i]=new ColorBinding(frostRenderers[i]);}
            foreach(var binding in bindings)binding?.Capture();
        }
        private void ApplyCold(){if(bindings!=null)foreach(var binding in bindings)binding?.Apply();coldApplied=true;}
        private void RestoreColors(){if(coldApplied&&bindings!=null)foreach(var binding in bindings)binding?.Restore();coldApplied=false;}
        private void OnDisable()=>ResetForReuse();
        /// <summary>槽位独立覆盖，归还全部原属性；不为反复命中创建材质实例。</summary>
        private sealed class ColorBinding
        {
            private readonly Renderer renderer;
            private readonly MaterialPropertyBlock[] originals,working;
            private readonly Color[] colors;
            public ColorBinding(Renderer source)
            {
                renderer=source;int count=source.sharedMaterials.Length;originals=new MaterialPropertyBlock[count];working=new MaterialPropertyBlock[count];colors=new Color[count];
                for(int i=0;i<count;i++){originals[i]=new MaterialPropertyBlock();working[i]=new MaterialPropertyBlock();}
            }
            public void Capture()
            {
                if(renderer==null)return;var materials=renderer.sharedMaterials;
                for(int i=0;i<originals.Length;i++)
                {
                    renderer.GetPropertyBlock(originals[i],i);renderer.GetPropertyBlock(working[i],i);
                    colors[i]=materials[i]!=null&&materials[i].HasProperty("_BaseColor")?materials[i].GetColor("_BaseColor"):Color.white;
                    if(originals[i].HasColor("_BaseColor"))colors[i]=originals[i].GetColor("_BaseColor");
                }
            }
            public void Apply()
            {
                if(renderer==null)return;
                for(int i=0;i<working.Length;i++)
                {var color=Color.Lerp(colors[i],new Color(.4f,.85f,1),.24f);working[i].SetColor("_BaseColor",color);working[i].SetColor("_Color",color);renderer.SetPropertyBlock(working[i],i);}
            }
            public void Restore()
            {if(renderer!=null)for(int i=0;i<originals.Length;i++)renderer.SetPropertyBlock(originals[i].isEmpty?null:originals[i],i);}
        }
    }
}
