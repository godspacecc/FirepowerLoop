using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    /// <summary>已确认宝箱动画的纯显示组件；仅采样权威时间和已领身份，不产生奖励。</summary>
    public sealed class ArcadeRoundRewardVisual:MonoBehaviour
    {
        /// <summary>依次显示坠落、爆箱、散落和地面奖励；加载迟到也直接采样当前阶段。</summary>
        public void Apply(ArcadeRoundRewardState state,ArcadeRoundRewardTuning tuning)
        {
            if(state==null||tuning==null)return;
            float time=state.ElapsedSeconds,burst=tuning.FallSeconds+tuning.ImpactSeconds;
            var chest=transform.Find("Chest");chest.gameObject.SetActive(time<burst&&state.Phase!=ArcadeRoundRewardPhase.Expired);
            float fall=1-Mathf.Clamp01(time/tuning.FallSeconds);chest.localPosition=new Vector3(0,.25f+4*fall*fall,0);
            float progress=Mathf.Clamp01((time-burst)/Mathf.Max(.01f,tuning.ScatterSeconds-.1f));
            foreach(var item in state.Items)
            {
                var coin=transform.Find("Reward"+item.Id);
                if(coin==null){coin=Instantiate(transform.Find(item.GoldBar?"Reward0":"Reward1"),transform);coin.name="Reward"+item.Id;}
                var target=new Vector3(item.X-state.X,.13f,item.Z-state.Z);
                coin.localPosition=Vector3.Lerp(new Vector3(0,.55f,0),target,progress)+Vector3.up*(Mathf.Sin(progress*Mathf.PI)*(1+(item.Id%4)*.13f));
                coin.localRotation=Quaternion.Euler(item.GoldBar?0:90,progress*240+item.Id*37,0);
                coin.gameObject.SetActive(time>=burst&&!state.Collected.Contains(item.Id)&&state.Phase!=ArcadeRoundRewardPhase.Expired);
            }
            for(int i=0;i<8;i++)
            {var part=transform.Find("Debris"+i);part.gameObject.SetActive(time>=burst&&time<burst+1.2f&&state.Phase!=ArcadeRoundRewardPhase.Expired);
                float a=i*Mathf.PI*.25f,spread=Mathf.Clamp01((time-burst)/1.2f)*2;
                part.localPosition=new Vector3(Mathf.Cos(a)*spread,.4f+Mathf.Sin(spread*Mathf.PI*.5f),Mathf.Sin(a)*spread);part.localRotation=Quaternion.Euler(time*110,i*45,time*40);}
        }
    }
}
