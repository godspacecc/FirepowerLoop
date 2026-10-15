using UnityEngine;
using UnityEngine.Audio;

namespace Game
{
    /// <summary>
    /// 保存音频系统需要的根节点绑定，明确音乐和音效 AudioSource 的所有权来自场景对象。
    /// </summary>
    public sealed class AudioRootBindings : MonoBehaviour
    {
        public AudioMixer Mixer;
        public AudioMixerGroup MusicGroup;
        public AudioMixerGroup SoundGroup;
        public AudioSource MusicSourceA;
        public AudioSource MusicSourceB;
        public Transform SoundPoolRoot;
        public Transform PlayingSoundRoot;

        public bool IsValid => Mixer != null && MusicGroup != null && SoundGroup != null &&
            MusicSourceA != null && MusicSourceB != null && MusicSourceA != MusicSourceB &&
            SoundPoolRoot != null && PlayingSoundRoot != null;
    }
}
