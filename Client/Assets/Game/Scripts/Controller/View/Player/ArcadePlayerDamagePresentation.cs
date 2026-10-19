using System;
using UnityEngine;

namespace Game
{
    /// <summary>角色独占的受击闪点、落地灰尘和救援标记；由现有反馈生命周期推进。</summary>
    internal sealed class ArcadePlayerDamagePresentation : IDisposable
    {
        /// <summary>随当前角色销毁的表现根；三个阶段始终复用同一组资源。</summary>
        private readonly GameObject root;
        private readonly Transform hit,landing,rescue;
        private readonly Renderer[] renderers;
        private readonly Color[] colors;
        private readonly MaterialPropertyBlock block=new();
        private static readonly int BaseColor=Shader.PropertyToID("_BaseColor");
        // 这些时长只决定特效分段，不控制死亡、无敌或复活规则。
        private const float HitSeconds=.18f, LandingDelay=.32f, DustSeconds=.55f;
        /// <summary>只由反馈帧推进的表现年龄，零时间步保持暂停画面。</summary>
        private float hitAge=HitSeconds,deathAge=10f;
        /// <summary>存活状态的只读镜像，不能用于裁决死亡或救援。</summary>
        private bool alive;
        private Transform recoilBone;
        private Quaternion beforeRecoil,afterRecoil;
        /// <summary>记录本次叠加姿态，恢复时不覆盖 Animator 新采样的骨骼。</summary>
        private bool hasRecoil;

        /// <summary>初始化专属模板；克隆已有角色时复用根并清除继承的瞬时表现。</summary>
        public ArcadePlayerDamagePresentation(Transform owner,GameObject template,bool initiallyAlive)
        {
            var existing=owner.Find("ArcadeDamage");
            root=existing!=null?existing.gameObject:UnityEngine.Object.Instantiate(template,owner,false);
            root.name="ArcadeDamage";hit=root.transform.Find("Hit");landing=root.transform.Find("Landing");rescue=root.transform.Find("Rescue");
            renderers=root.GetComponentsInChildren<Renderer>(true);colors=new Color[renderers.Length];
            for(int i=0;i<renderers.Length;i++)colors[i]=renderers[i].sharedMaterial.GetColor(BaseColor);
            alive=initiallyAlive;Reset();root.SetActive(true);
        }
        /// <summary>只在实际受伤时调用；重复受击复用同一组，不能生成额外角色或影响碰撞。</summary>
        public void Hurt()
        {if(!alive)return;root.SetActive(true);hitAge=0;Render();}
        /// <summary>首次同步死亡只建立基线；有效存活到死亡转换才播放致命闪光与落尘。</summary>
        public void SetAlive(bool value,bool playTransition)
        {
            if(value==alive)return;
            alive=value;root.SetActive(true);UndoRecoil();
            if(value){Reset();root.SetActive(true);return;}
            rescue.gameObject.SetActive(false);
            if(playTransition){hitAge=0;deathAge=0;}else{hitAge=HitSeconds;deathAge=10;}
            Render();
        }
        /// <summary>只镜像现有服务器资格，存活角色不显示倒地救援提示。</summary>
        public void SetRescue(bool active)
        {rescue.gameObject.SetActive(active&&!alive);if(active&&!alive)root.SetActive(true);}
        /// <summary>沿用玩家反馈时钟推进闪点及落尘，不创建独立 Update。</summary>
        public void Tick(float seconds)
        {
            if(seconds<=0)return;
            hitAge+=seconds;deathAge+=seconds;Render();
        }
        /// <summary>按当前年龄更新显隐、扩散和透明度，材质参数按角色独立。</summary>
        private void Render()
        {
            bool hitActive=hitAge<HitSeconds;hit.gameObject.SetActive(hitActive);
            float hp=Mathf.Clamp01(hitAge/HitSeconds);hit.localScale=Vector3.one*Mathf.Lerp(.55f,1.15f,hp);
            float dustProgress=Mathf.Clamp01((deathAge-LandingDelay)/DustSeconds);
            landing.gameObject.SetActive(!alive&&deathAge>=LandingDelay&&deathAge<LandingDelay+DustSeconds);
            landing.localScale=Vector3.one*Mathf.Lerp(.5f,1.6f,dustProgress);
            for(int i=0;i<renderers.Length;i++)
            {
                var renderer=renderers[i];bool isHit=renderer.transform.IsChildOf(hit);
                if(!isHit&&!renderer.transform.IsChildOf(landing))continue;
                var color=colors[i];color.a*=isHit?1-hp:1-dustProgress;
                block.SetColor(BaseColor,color);renderer.SetPropertyBlock(block);
            }
        }
        /// <summary>仅叠加小幅上身震动；相同姿态反复求值先撤销上次偏移，暂停时不累积。</summary>
        public void ApplyHurtPose(Animator animator)
        {
            UndoRecoil();
            if(!alive||hitAge>=HitSeconds||animator==null||!animator.isHuman)return;
            if(recoilBone==null)recoilBone=animator.GetBoneTransform(HumanBodyBones.Chest);
            if(recoilBone==null)return;
            beforeRecoil=recoilBone.localRotation;
            afterRecoil=beforeRecoil*Quaternion.Euler(-4f*Mathf.Sin(Mathf.Clamp01(hitAge/HitSeconds)*Mathf.PI),0,0);
            recoilBone.localRotation=afterRecoil;hasRecoil=true;
        }
        /// <summary>只撤销仍保持原偏移的骨骼，避免回写过期动画姿态。</summary>
        private void UndoRecoil()
        {
            if(hasRecoil&&recoilBone!=null&&Quaternion.Angle(recoilBone.localRotation,afterRecoil)<.01f)recoilBone.localRotation=beforeRecoil;
            hasRecoil=false;
        }
        /// <summary>禁用、离场与复活都撤销纯表现，模板资源由角色 Prefab 句柄持有。</summary>
        public void Reset(){UndoRecoil();hitAge=HitSeconds;deathAge=10;hit.gameObject.SetActive(false);landing.gameObject.SetActive(false);rescue.gameObject.SetActive(false);root.SetActive(false);}
        /// <summary>释放时立即移出角色子树，防止同帧重建复用等待帧尾销毁的旧实例；重复释放安全。</summary>
        public void Dispose()
        {
            if(root==null)return;
            Reset();
            root.transform.SetParent(null,false);
            if(Application.isPlaying)UnityEngine.Object.Destroy(root);else UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
