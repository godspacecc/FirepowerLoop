using UnityEngine;

namespace Game
{
    /// <summary>地图武器空投的纯表现：权威进度决定下降高度和开盖姿态，不拥有计时或拾取逻辑。</summary>
    public sealed class ArcadeWeaponDeliveryView:MonoBehaviour
    {
        [SerializeField] private Transform lid;
        [SerializeField] private ParticleSystem landingDust;
        [SerializeField] private float dropHeight=8f;
        /// <summary>仅用于落地尘土去重，首次收到已落地快照不补播。</summary>
        private float lastProgress=-1;
        public bool WeaponVisible=>lastProgress>=.9f;

        /// <summary>前三季度加速下落，落地后自动开盖；重复快照不重播落地反馈。</summary>
        public void SetProgress(float remaining,float duration)
        {
            float progress=duration>0&&!float.IsNaN(remaining)?Mathf.Clamp01(1-remaining/duration):1;
            gameObject.SetActive(true);
            float fall=Mathf.Clamp01(progress/.75f);
            // 通用掉落根自带0.45缩放；空投高度与箱体尺寸使用场景米，不能继承成悬空小箱子。
            var parentScale=transform.parent.lossyScale;
            transform.localScale=new Vector3(1/Mathf.Max(.001f,Mathf.Abs(parentScale.x)),1/Mathf.Max(.001f,Mathf.Abs(parentScale.y)),1/Mathf.Max(.001f,Mathf.Abs(parentScale.z)));
            transform.position=transform.parent.position+Vector3.up*(-.3f+dropHeight*(1-fall*fall));
            // 掉落根仍负责武器慢转，箱体保持固定朝向，不随武器绕地旋转。
            transform.rotation=Quaternion.identity;
            if(lid!=null)lid.localRotation=Quaternion.Euler(115*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.75f,1,progress)),0,0);
            if(lastProgress>=0&&lastProgress<.75f&&progress>=.75f&&landingDust!=null)landingDust.Emit(14);
            lastProgress=progress;
        }

        /// <summary>回池或换地图清除落地记忆与尘土，下一把武器重新从空中开始。</summary>
        public void ResetPresentation()
        {
            lastProgress=-1;if(lid!=null)lid.localRotation=Quaternion.identity;
            if(landingDust!=null)landingDust.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false);
        }
    }
}
