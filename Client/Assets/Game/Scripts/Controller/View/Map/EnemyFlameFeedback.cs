using UnityEngine;

namespace Game
{
    /// <summary>敌人附着火焰、火星和死亡余烟；只消费已裁决状态，沿用EnemyCtrl表现帧与对象池。</summary>
    public sealed class EnemyFlameFeedback : MonoBehaviour
    {
        [SerializeField] private ParticleSystem bodyFlame;
        [SerializeField] private ParticleSystem embers;
        [SerializeField] private ParticleSystem deathSmoke;
        /// <summary>本次实例的表现时钟，回池显式清空，不保存或造成伤害。</summary>
        private float hitRemaining,deathRemaining;

        public bool IsBurning=>hitRemaining>0||deathRemaining>0;

        /// <summary>权威剩余量上升表示新命中，火星仅在该次刷新时迸发。</summary>
        public void SetRemaining(float seconds)
        {
            if(deathRemaining>0)return;
            seconds=float.IsNaN(seconds)||float.IsInfinity(seconds)?0:Mathf.Max(0,seconds);
            bool refreshed=seconds>hitRemaining+.03f;
            hitRemaining=seconds;
            if(seconds>0)
            {
                if(bodyFlame!=null&&!bodyFlame.isEmitting)bodyFlame.Play(false);
                if(refreshed&&embers!=null)embers.Emit(8);
            }
            else StopFlame();
        }

        /// <summary>火焰击杀在倒地时短暂增强火焰和火星，余烟自动消散；普通死亡不补播。</summary>
        public void PlayDeath(bool flameKill)
        {
            if(!flameKill){hitRemaining=0;StopFlame();return;}
            if(deathRemaining>0)return;
            hitRemaining=0;deathRemaining=.8f;
            if(bodyFlame!=null){bodyFlame.Play(false);bodyFlame.Emit(20);}
            if(embers!=null)embers.Emit(36);
            if(deathSmoke!=null)deathSmoke.Play(false);
        }

        /// <summary>由现有敌人表现生命周期推进，暂停的零步长不消耗火焰时长。</summary>
        public void Tick(float seconds)
        {
            if(seconds<=0)return;
            if(deathRemaining>0)deathRemaining=Mathf.Max(0,deathRemaining-seconds);
            else hitRemaining=Mathf.Max(0,hitRemaining-seconds);
            if(!IsBurning)StopFlame();
        }

        /// <summary>回池、禁用或切图统一清掉全部粒子，避免下一名敌人继承着火状态。</summary>
        public void ResetForReuse()
        {
            hitRemaining=deathRemaining=0;
            Clear(bodyFlame);Clear(embers);Clear(deathSmoke);
        }
        private void StopFlame(){if(bodyFlame!=null&&bodyFlame.isEmitting)bodyFlame.Stop(false,ParticleSystemStopBehavior.StopEmitting);}
        private static void Clear(ParticleSystem particles){if(particles!=null)particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
        private void OnDisable()=>ResetForReuse();
    }
}
