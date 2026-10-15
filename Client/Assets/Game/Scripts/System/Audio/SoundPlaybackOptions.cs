using UnityEngine;

namespace Game
{
    /// <summary>单次空间音效的表现参数；开始期限为不受暂停缩放影响的秒数，非正值表示不限时。</summary>
    public readonly struct SoundPlaybackOptions
    {
        public readonly float Gain, MinDistance, MaximumStartDelay;
        public SoundPlaybackOptions(float gain, float minDistance, float maximumStartDelay)
        {
            Gain = Mathf.Clamp01(gain);
            MinDistance = Mathf.Max(0f, minDistance);
            MaximumStartDelay = Mathf.Max(0f, maximumStartDelay);
        }
    }
}
