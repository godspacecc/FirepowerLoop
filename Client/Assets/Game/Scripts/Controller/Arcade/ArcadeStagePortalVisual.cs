using UnityEngine;
namespace Game
{
    /// <summary>整关传送门的纯显示动画，接触推进由规则系统负责。</summary>
    public sealed class ArcadeStagePortalVisual:MonoBehaviour
    {
        /// <summary>按活动秒采样出现与旋转，暂停及迟到加载都不会改变真实出口条件。</summary>
        public void Apply(float seconds,float appearSeconds)
        {
            var portal=transform.Find("Portal");float amount=Mathf.SmoothStep(0,1,Mathf.Clamp01(seconds/Mathf.Max(.01f,appearSeconds)));
            portal.gameObject.SetActive(amount>0);portal.localScale=Vector3.one*amount;
            foreach(Transform child in portal)if(child.name.StartsWith("Flow"))child.localRotation=Quaternion.Euler(0,0,seconds*35);
        }
    }
}
