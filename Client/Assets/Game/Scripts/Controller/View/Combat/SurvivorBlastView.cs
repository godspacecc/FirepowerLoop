using UnityEngine;
namespace Game
{
    /// <summary>短冲击特效只采样权威位置、半径与进度；不拥有碰撞或战斗计时。</summary>
    public sealed class SurvivorBlastView : MonoBehaviour
    {
        [SerializeField] private LineRenderer ring;
        [SerializeField] private LineRenderer[] sparks;
        /// <summary>复用模板固定拓扑；两个表现元素均随世界半径缩放，不生成每帧资源。</summary>
        public void Apply(Vector3 position,float radius,float progress)
        {
            progress=Mathf.Clamp01(progress);float distance=radius*progress;
            var color=new Color(1,.65f,.2f,1-progress);
            ring.startColor=ring.endColor=color;
            for(int i=0;i<ring.positionCount;i++)
            {
                float a=i*Mathf.PI*2/(ring.positionCount-1);
                ring.SetPosition(i,position+new Vector3(Mathf.Cos(a)*distance,.08f,Mathf.Sin(a)*distance));
            }
            for(int i=0;i<sparks.Length;i++)
            {
                float a=i*Mathf.PI*2/sparks.Length;var direction=new Vector3(Mathf.Cos(a),.2f,Mathf.Sin(a));
                sparks[i].startColor=color;sparks[i].endColor=new Color(1,.75f,.3f,0);
                sparks[i].SetPosition(0,position+Vector3.up*.8f+direction*distance);
                sparks[i].SetPosition(1,position+Vector3.up*.8f+direction*distance*.6f);
            }
        }
    }
}
