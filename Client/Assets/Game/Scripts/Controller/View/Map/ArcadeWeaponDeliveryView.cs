using UnityEngine;

namespace Game
{
    /// <summary>地图武器空投的纯表现：权威进度决定下落、弹出和箱体消失，不拥有计时或拾取逻辑。</summary>
    public sealed class ArcadeWeaponDeliveryView:MonoBehaviour
    {
        [SerializeField] private Transform lid;
        [SerializeField] private ParticleSystem landingDust;
        [SerializeField] private float dropHeight=8f;
        /// <summary>已确认的箱体缩小比例，使用世界尺寸，避免遮挡放大的空投武器。</summary>
        [SerializeField] private float crateScale=.6f;
        /// <summary>仅用于落地尘土去重，首次收到已落地快照不补播。</summary>
        private float lastProgress=-1;
        /// <summary>只缓存箱体实体网格，隐藏时保留落地尘土，不重复扫描或禁用粒子。</summary>
        private MeshRenderer[] crateRenderers;
        /// <summary>落地即弹出武器；可否拾取仍由权威剩余时间决定。</summary>
        public bool WeaponVisible=>lastProgress>=.75f;
        /// <summary>已采用弹出弧线的世界高度增量；晚加载完成态直接归零，不补播动画。</summary>
        public float WeaponPopLift
        {
            get
            {
                float pop=Mathf.InverseLerp(.75f,1,lastProgress);
                return Mathf.Lerp(-.35f,0,Mathf.SmoothStep(0,1,pop))+Mathf.Sin(pop*Mathf.PI)*.85f;
            }
        }

        /// <summary>前四分之三加速下落，落地即隐藏箱体并弹出武器；重复快照不重播反馈。</summary>
        public void SetProgress(float remaining,float duration)
        {
            float progress=duration>0&&!float.IsNaN(remaining)?Mathf.Clamp01(1-remaining/duration):1;
            gameObject.SetActive(true);
            float fall=Mathf.Clamp01(progress/.75f);
            // 通用掉落根自带0.45缩放；空投高度与箱体尺寸使用场景米，不能继承成悬空小箱子。
            var parentScale=transform.parent.lossyScale;
            transform.localScale=new Vector3(crateScale/Mathf.Max(.001f,Mathf.Abs(parentScale.x)),crateScale/Mathf.Max(.001f,Mathf.Abs(parentScale.y)),crateScale/Mathf.Max(.001f,Mathf.Abs(parentScale.z)));
            transform.position=transform.parent.position+Vector3.up*(-.3f+dropHeight*(1-fall*fall));
            // 掉落根仍负责武器慢转，箱体保持固定朝向，不随武器绕地旋转。
            transform.rotation=Quaternion.identity;
            if(lid!=null)lid.localRotation=Quaternion.Euler(115*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.75f,1,progress)),0,0);
            if(lastProgress>=0&&lastProgress<.75f&&progress>=.75f&&landingDust!=null)landingDust.Emit(14);
            lastProgress=progress;
            SetCrateVisible(!WeaponVisible);
        }

        /// <summary>箱体显隐与尘土分离，回池时恢复供下一次空投使用。</summary>
        private void SetCrateVisible(bool visible)
        {
            if(crateRenderers==null)crateRenderers=GetComponentsInChildren<MeshRenderer>(true);
            foreach(var renderer in crateRenderers)if(renderer!=null)renderer.enabled=visible;
        }

        /// <summary>回池或换地图清除落地记忆与尘土，下一把武器重新从空中开始。</summary>
        public void ResetPresentation()
        {
            lastProgress=-1;if(lid!=null)lid.localRotation=Quaternion.identity;
            SetCrateVisible(true);
            if(landingDust!=null)landingDust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false);
        }
    }
}
