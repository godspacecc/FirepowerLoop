using UnityEngine;
namespace Game
{
    /// <summary>已确认粘液外观按领域年龄采样；暂停不自行播放。</summary>
    public sealed class SurvivorSlimeZoneVisual : MonoBehaviour
    {
        /// <summary>液面与十二颗液滴来自正式模板。</summary>
        public Transform Pool;
        public Transform[] Drops;
        public bool IsValid=>Pool!=null&&Drops!=null&&Drops.Length==12&&System.Array.TrueForAll(Drops,x=>x!=null);
        public void ResetVisual(){Sample(1.2f,0);}
        /// <summary>液面半径与真实减速边界一致，液滴沿用采用预览位置。</summary>
        public void Sample(float radius,float age)
        {
            float spread=Mathf.Lerp(.18f,1,Mathf.Clamp01(age/.16f));
            Pool.localScale=Vector3.one*(radius/1.2f)*spread;
            for(int i=0;i<Drops.Length;i++) {
                float ang=i*2.4f;
                Drops[i].localPosition=new Vector3(Mathf.Cos(ang)*(.3f+i*.055f),.04f,Mathf.Sin(ang)*(.3f+i*.055f));
            }
        }
    }
}
