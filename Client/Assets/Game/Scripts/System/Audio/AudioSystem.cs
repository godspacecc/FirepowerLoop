using System.Collections.Generic;
using QFramework;
using UnityEngine;
using UnityEngine.Audio;

namespace Game
{
    /// <summary>
    /// 管理音乐和音效播放、音量应用和地图归属声音清理；资源 Handle 由播放句柄持有并在停止时释放。
    /// </summary>
    public sealed class AudioSystem : AbstractSystem
    {
        private const int PrewarmSoundSources = 8;
        private const int MaxSoundSources = 32;

        private sealed class PlayingSound
        {
            public AudioPlaybackHandle PlaybackHandle;
            public ResourceLoadHandle ResourceHandle;
            public bool ResourceHandleAssigned;
            public bool ReleaseWhenAssigned;
            public bool Released;
            public AudioSource Source;
            public bool MapOwned;
            public bool Spatial;
            public bool UiSound;
            public bool PausedBySystem;
            public int Priority;
            public float Gain = 1f;
            // 循环及最新世界坐标由播放句柄持有，加载回调不能使用已经过期的位置。
            public bool Loop;
            public Vector3 Position;
            // 实时时间戳；0 表示原有音效调用不限开始时间。
            public float StartDeadline;
        }

        private sealed class MusicSlot
        {
            public AudioSource Source;
            public MusicLoadRequest Request;
            public ResourceLoadHandle Handle;
            public string Address;
            public float Fade = 1f;
        }

        private sealed class MusicLoadRequest
        {
            public int Generation;
            public string Address;
            public ResourceLoadHandle Handle;
            public bool HandleAssigned;
            public bool ReleaseWhenAssigned;
            public bool Released;
            public MusicSlot OwnerSlot;
        }

        private readonly Dictionary<long, PlayingSound> sounds = new Dictionary<long, PlayingSound>();
        private readonly Queue<AudioSource> sourcePool = new Queue<AudioSource>();
        private long nextId;
        private int musicGeneration;
        private string currentMusicAddress;
        private MusicSlot musicSlotA;
        private MusicSlot musicSlotB;
        private MusicSlot activeMusicSlot;
        private MusicLoadRequest pendingMusicRequest;
        private Transform soundPoolRoot;
        private Transform playingRoot;
        private AudioMixer mixer;
        private AudioMixerGroup musicGroup;
        private AudioMixerGroup soundGroup;
        private bool bindingsValid;
        private float musicFadeAStart;
        private float musicFadeBStart;
        private float musicFadeATarget = 1f;
        private float musicFadeBTarget = 1f;
        private float musicFadeDuration;
        private float musicFadeElapsed;
        private bool musicFadeStopSilentSources;
        private static bool forceMissingBindingsForTests;
        private bool worldSoundsPaused;

        public int ActiveSoundCount => sounds.Count;
        public int PooledSourceCount => sourcePool.Count;

        public static void SetBindingsUnavailableForTests(bool unavailable)
        {
            forceMissingBindingsForTests = unavailable;
        }

        protected override void OnInit()
        {
            LoadBindings();
            ApplyVolumes();
            this.RegisterEvent<AppStateChangedEvent>(OnAppStateChanged);
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(Update);
            StopMusic(0f);
            foreach (var sound in new List<PlayingSound>(sounds.Values))
            {
                Stop(sound.PlaybackHandle);
            }

            while (sourcePool.Count > 0)
            {
                var source = sourcePool.Dequeue();
                if (source != null)
                {
                    DestroyAudioObject(source.gameObject);
                }
            }

            pendingMusicRequest = null;
            musicSlotA = null;
            musicSlotB = null;
            activeMusicSlot = null;
        }

        public void SetMixerForTests(AudioMixer mixer)
        {
            this.mixer = mixer;
            ApplyVolumes();
        }

        public void ApplyVolumes()
        {
            var settings = this.GetModel<AudioSettingsModel>().ToData();
            var master = settings.Mute ? 0f : settings.MasterVolume;
            var music = settings.Mute ? 0f : settings.MusicVolume;
            var sound = settings.Mute ? 0f : settings.SoundVolume;
            if (mixer != null)
            {
                mixer.SetFloat("MasterVolumeDb", AudioVolumeUtility.LinearToDb(master));
                mixer.SetFloat("MusicVolumeDb", AudioVolumeUtility.LinearToDb(settings.MusicVolume));
                mixer.SetFloat("SoundVolumeDb", AudioVolumeUtility.LinearToDb(settings.SoundVolume));
            }

            ApplyMusicVolumes(mixer != null ? 1f : master * music);
            foreach (var soundEntry in sounds.Values)
            {
                if (soundEntry.Source != null)
                {
                    soundEntry.Source.volume = (mixer != null ? 1f : master * sound) * soundEntry.Gain;
                }
            }
        }

        /// <summary>
        /// 加载并切换背景音乐；新资源句柄归音乐槽持有，旧槽在淡出完成后释放。
        /// </summary>
        public void PlayMusic(string soundName, float fadeSeconds = 0.5f)
        {
            if (!ResourceAddress.TryFromFileName(ResourceAddressKind.Music, soundName, false, out var address, out var addressError))
            {
                Log.Warning($"[AudioSystem] 音乐播放请求已忽略：原值 {soundName ?? "<null>"}；{addressError}");
                return;
            }

            if (string.IsNullOrWhiteSpace(address) || address == currentMusicAddress ||
                (pendingMusicRequest != null && pendingMusicRequest.Address == address))
            {
                return;
            }

            if (!bindingsValid)
            {
                Log.Warning("[AudioSystem] 音乐播放请求已忽略：AudioRootBindings 缺失或无效。");
                return;
            }

            CancelPendingMusicRequest();
            var requestGeneration = ++musicGeneration;
            var request = new MusicLoadRequest
            {
                Generation = requestGeneration,
                Address = address
            };
            pendingMusicRequest = request;
            var loadedSynchronously = false;
            var returnedHandle = ResourceLoader.LoadAssetAsync<AudioClip>(address, clip =>
            {
                loadedSynchronously = true;
                if (clip == null)
                {
                    if (pendingMusicRequest == request)
                    {
                        pendingMusicRequest = null;
                    }

                    ReleaseMusicRequest(request);
                    return;
                }

                if (pendingMusicRequest != request || requestGeneration != musicGeneration)
                {
                    ReleaseMusicRequest(request);
                    return;
                }

                var nextSlot = activeMusicSlot == musicSlotA ? musicSlotB : musicSlotA;
                ReleaseMusicSlot(nextSlot);
                request.OwnerSlot = nextSlot;
                nextSlot.Request = request;
                nextSlot.Handle = request.Handle;
                nextSlot.Address = request.Address;
                nextSlot.Source.clip = clip;
                nextSlot.Source.loop = true;
                nextSlot.Source.spatialBlend = 0f;
                nextSlot.Source.outputAudioMixerGroup = musicGroup;
                SetMusicFade(nextSlot, fadeSeconds > 0f ? 0f : 1f);
                ApplyMusicVolumes();
                nextSlot.Source.Play();
                if (activeMusicSlot != null && activeMusicSlot != nextSlot && fadeSeconds <= 0f)
                {
                    ReleaseMusicSlot(activeMusicSlot);
                }

                BeginMusicFade(nextSlot, activeMusicSlot, fadeSeconds, true);
                activeMusicSlot = nextSlot;
                currentMusicAddress = address;
                pendingMusicRequest = null;
            });
            request.Handle = returnedHandle;
            request.HandleAssigned = true;
            if (request.ReleaseWhenAssigned)
            {
                ReleaseMusicRequest(request);
            }
            else if (loadedSynchronously && request.OwnerSlot != null)
            {
                request.OwnerSlot.Handle = returnedHandle;
            }
        }

        public void StopMusic(float fadeSeconds = 0.5f)
        {
            musicGeneration++;
            currentMusicAddress = null;
            CancelPendingMusicRequest();
            if (fadeSeconds > 0f && ((musicSlotA?.Source != null && musicSlotA.Source.isPlaying) || (musicSlotB?.Source != null && musicSlotB.Source.isPlaying)))
            {
                BeginMusicFade(null, null, fadeSeconds, true);
                activeMusicSlot = null;
                return;
            }

            ReleaseMusicSlot(musicSlotA);
            ReleaseMusicSlot(musicSlotB);
            activeMusicSlot = null;
        }

        private void CancelPendingMusicRequest()
        {
            if (pendingMusicRequest == null)
            {
                return;
            }

            ReleaseMusicRequest(pendingMusicRequest);
            pendingMusicRequest = null;
        }

        public AudioPlaybackHandle PlayUiSound(string soundName)
        {
            return PlaySoundByName(soundName, "UI 音效", 100, false, true, Vector3.zero, false, 20f);
        }

        /// <summary>
        /// 播放普通音效并返回播放句柄；资源加载完成前取消时会延迟释放归属句柄。
        /// </summary>
        public AudioPlaybackHandle PlaySound(string soundName, int priority = 0)
        {
            return PlaySoundByName(soundName, "音效", priority, false, false, Vector3.zero, false, 20f);
        }

        public AudioPlaybackHandle PlaySoundAt(string soundName, Vector3 position, float maxDistance = 20f, int priority = 0)
        {
            return PlaySoundByName(soundName, "空间音效", priority, true, false, position, true, maxDistance);
        }

        /// <summary>带单次增益、近距和开始期限的空间音效；过期或暂停期间的迟到加载会静音释放。</summary>
        public AudioPlaybackHandle PlaySoundAt(string soundName, Vector3 position, float maxDistance,
            int priority, SoundPlaybackOptions options)
        {
            if (!ResourceAddress.TryFromFileName(ResourceAddressKind.Sound, soundName, false, out var address, out _))
                return AudioPlaybackHandle.Empty;
            return PlaySoundInternal(address, priority, true, false, position, true, maxDistance, options);
        }

        private AudioPlaybackHandle PlaySoundByName(string soundName, string playbackKind, int priority, bool mapOwned, bool uiSound, Vector3 position, bool spatial, float maxDistance)
        {
            if (!ResourceAddress.TryFromFileName(ResourceAddressKind.Sound, soundName, false, out var address, out var addressError))
            {
                Log.Warning($"[AudioSystem] {playbackKind}播放请求已忽略：原值 {soundName ?? "<null>"}；{addressError}");
                return AudioPlaybackHandle.Empty;
            }

            return PlaySoundInternal(address, priority, mapOwned, uiSound, position, spatial, maxDistance);
        }

        /// <summary>播放地图归属空间循环音；调用者持有句柄，在对象停用或动作取消时停止。</summary>
        public AudioPlaybackHandle PlayLoopAt(string soundName, Vector3 position, float maxDistance,
            int priority, SoundPlaybackOptions options)
        {
            if (!ResourceAddress.TryFromFileName(ResourceAddressKind.Sound, soundName, false, out var address, out _))
                return AudioPlaybackHandle.Empty;
            return PlaySoundInternal(address, priority, true, false, position, true, maxDistance, options, true);
        }

        /// <summary>同步活动或加载中的声音坐标；已释放的句柄无副作用。</summary>
        public void SetSoundPosition(AudioPlaybackHandle handle, Vector3 position)
        {
            if (!sounds.TryGetValue(handle.Id, out var sound)) return;
            sound.Position = position;
            if (sound.Source != null) sound.Source.transform.position = position;
        }

        /// <summary>更新持续空间声的实际增益；加载中也保留最新值，音量设置刷新不会覆盖距离衰减。</summary>
        public void SetSoundGain(AudioPlaybackHandle handle, float gain)
        {
            if (!sounds.TryGetValue(handle.Id, out var sound)) return;
            sound.Gain = float.IsNaN(gain) || float.IsInfinity(gain) ? 0f : Mathf.Clamp01(gain);
            if (sound.Source != null) sound.Source.volume = CurrentSoundVolume() * sound.Gain;
        }

        /// <summary>同时包含加载中和正在播放的句柄，供持续音效在失败或音源抢占后恢复。</summary>
        public bool IsSoundActive(AudioPlaybackHandle handle) => handle.IsValid && sounds.ContainsKey(handle.Id);

        public void Stop(AudioPlaybackHandle handle)
        {
            if (!handle.IsValid || !sounds.TryGetValue(handle.Id, out var sound))
            {
                return;
            }

            sounds.Remove(handle.Id);
            if (sound.Source != null)
            {
                ReturnSource(sound.Source);
            }

            ReleaseSoundResource(sound);
        }

        public void StopMapOwnedSounds()
        {
            foreach (var sound in new List<PlayingSound>(sounds.Values))
            {
                if (sound.MapOwned)
                {
                    Stop(sound.PlaybackHandle);
                }
            }
        }

        private AudioPlaybackHandle PlaySoundInternal(string address, int priority, bool mapOwned, bool uiSound, Vector3 position, bool spatial, float maxDistance, SoundPlaybackOptions? options = null, bool loop = false)
        {
            if (string.IsNullOrWhiteSpace(address) || !bindingsValid)
            {
                if (!bindingsValid)
                {
                    Log.Warning("[AudioSystem] 音效播放请求已忽略：AudioRootBindings 缺失或无效。");
                }

                return AudioPlaybackHandle.Empty;
            }

            if (sounds.Count >= MaxSoundSources)
            {
                PlayingSound lowest = null;
                foreach (var sound in sounds.Values)
                {
                    if (lowest == null || sound.Priority < lowest.Priority)
                    {
                        lowest = sound;
                    }
                }

                if (lowest == null || priority <= lowest.Priority)
                {
                    return AudioPlaybackHandle.Empty;
                }

                Stop(lowest.PlaybackHandle);
            }

            var handle = new AudioPlaybackHandle(++nextId);
            var playing = new PlayingSound
            {
                PlaybackHandle = handle,
                MapOwned = mapOwned,
                Spatial = spatial,
                UiSound = uiSound,
                Priority = priority,
                Gain = options?.Gain ?? 1f,
                Loop = loop,
                Position = position,
                StartDeadline = options.HasValue && options.Value.MaximumStartDelay > 0f
                    ? Time.realtimeSinceStartup + options.Value.MaximumStartDelay : 0f
            };
            sounds[handle.Id] = playing;
            var returnedHandle = ResourceLoader.LoadAssetAsync<AudioClip>(address, clip =>
            {
                if (!sounds.TryGetValue(handle.Id, out var live) || clip == null ||
                    !uiSound && worldSoundsPaused ||
                    playing.StartDeadline > 0f && Time.realtimeSinceStartup > playing.StartDeadline)
                {
                    sounds.Remove(handle.Id);
                    ReleaseSoundResource(playing);
                    return;
                }

                live.Source = RentSource($"SoundSource-{handle.Id}");
                live.Source.clip = clip;
                live.Source.loop = live.Loop;
                live.Source.spatialBlend = spatial ? 1f : 0f;
                live.Source.volume = CurrentSoundVolume() * live.Gain;
                live.Source.transform.position = live.Position;
                live.Source.maxDistance = maxDistance;
                live.Source.minDistance = Mathf.Min(maxDistance, options?.MinDistance ?? 1f);
                live.Source.outputAudioMixerGroup = soundGroup;
                live.Source.Play();
            });
            playing.ResourceHandle = returnedHandle;
            playing.ResourceHandleAssigned = true;
            if (playing.ReleaseWhenAssigned)
            {
                ReleaseSoundResource(playing);
            }

            return handle;
        }

        private void Update()
        {
            UpdateMusicFade();
            foreach (var sound in new List<PlayingSound>(sounds.Values))
            {
                if (sound.Source == null && sound.StartDeadline > 0f && Time.realtimeSinceStartup > sound.StartDeadline)
                {
                    Stop(sound.PlaybackHandle);
                    continue;
                }
                if (sound.Source != null && !sound.Source.isPlaying && !sound.PausedBySystem)
                {
                    Stop(sound.PlaybackHandle);
                }
            }
        }

        private AudioSource RentSource(string name)
        {
            var source = sourcePool.Count > 0 ? sourcePool.Dequeue() : CreateSource(name, playingRoot);
            source.name = name;
            source.transform.SetParent(playingRoot, false);
            source.gameObject.SetActive(true);
            ResetSource(source);
            return source;
        }

        private void ReturnSource(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            ResetSource(source);
            source.gameObject.SetActive(false);
            source.transform.SetParent(soundPoolRoot, false);
            sourcePool.Enqueue(source);
        }

        private static void ResetSource(AudioSource source)
        {
            source.Stop();
            source.clip = null;
            source.loop = false;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = 0f;
            source.maxDistance = 20f;
            source.minDistance = 1f;
            source.outputAudioMixerGroup = null;
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
        }

        private void LoadBindings()
        {
            if (forceMissingBindingsForTests)
            {
                bindingsValid = false;
                return;
            }

            var bindings = Object.FindObjectOfType<AudioRootBindings>();
            if (bindings == null || !bindings.IsValid)
            {
                bindingsValid = false;
                return;
            }

            mixer = bindings.Mixer;
            musicGroup = bindings.MusicGroup;
            soundGroup = bindings.SoundGroup;
            soundPoolRoot = bindings.SoundPoolRoot;
            playingRoot = bindings.PlayingSoundRoot;
            musicSlotA = new MusicSlot { Source = bindings.MusicSourceA, Fade = 1f };
            musicSlotB = new MusicSlot { Source = bindings.MusicSourceB, Fade = 1f };
            musicSlotA.Source.outputAudioMixerGroup = musicGroup;
            musicSlotB.Source.outputAudioMixerGroup = musicGroup;
            bindingsValid = true;
            while (sourcePool.Count < PrewarmSoundSources)
            {
                var source = CreateSource($"PooledSoundSource-{sourcePool.Count + 1}", soundPoolRoot);
                source.gameObject.SetActive(false);
                sourcePool.Enqueue(source);
            }
        }

        private static AudioSource CreateSource(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.AddComponent<AudioSource>();
        }

        private static void DestroyAudioObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        private float CurrentMusicVolume()
        {
            var settings = this.GetModel<AudioSettingsModel>().ToData();
            return mixer != null ? 1f : settings.Mute ? 0f : settings.MasterVolume * settings.MusicVolume;
        }

        private float CurrentSoundVolume()
        {
            var settings = this.GetModel<AudioSettingsModel>().ToData();
            return mixer != null ? 1f : settings.Mute ? 0f : settings.MasterVolume * settings.SoundVolume;
        }

        private void BeginMusicFade(MusicSlot fadeIn, MusicSlot fadeOut, float duration, bool stopSilentSources)
        {
            musicFadeAStart = musicSlotA?.Fade ?? 0f;
            musicFadeBStart = musicSlotB?.Fade ?? 0f;
            musicFadeATarget = fadeIn == musicSlotA ? 1f : 0f;
            musicFadeBTarget = fadeIn == musicSlotB ? 1f : 0f;
            if (fadeOut == musicSlotA) musicFadeATarget = 0f;
            if (fadeOut == musicSlotB) musicFadeBTarget = 0f;
            musicFadeDuration = Mathf.Max(0f, duration);
            musicFadeElapsed = 0f;
            musicFadeStopSilentSources = stopSilentSources;
            if (musicFadeDuration <= 0f)
            {
                CompleteMusicFade();
            }
        }

        private void UpdateMusicFade()
        {
            if (musicFadeDuration <= 0f)
            {
                return;
            }

            musicFadeElapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(musicFadeElapsed / musicFadeDuration);
            if (musicSlotA != null) musicSlotA.Fade = Mathf.Lerp(musicFadeAStart, musicFadeATarget, t);
            if (musicSlotB != null) musicSlotB.Fade = Mathf.Lerp(musicFadeBStart, musicFadeBTarget, t);
            ApplyMusicVolumes();
            if (t >= 1f)
            {
                CompleteMusicFade();
            }
        }

        private void CompleteMusicFade()
        {
            if (musicSlotA != null) musicSlotA.Fade = musicFadeATarget;
            if (musicSlotB != null) musicSlotB.Fade = musicFadeBTarget;
            musicFadeDuration = 0f;
            ApplyMusicVolumes();
            if (!musicFadeStopSilentSources)
            {
                return;
            }

            StopIfSilent(musicSlotA);
            StopIfSilent(musicSlotB);
        }

        private void ApplyMusicVolumes()
        {
            ApplyMusicVolumes(CurrentMusicVolume());
        }

        private void ApplyMusicVolumes(float baseVolume)
        {
            if (musicSlotA?.Source != null) musicSlotA.Source.volume = baseVolume * musicSlotA.Fade;
            if (musicSlotB?.Source != null) musicSlotB.Source.volume = baseVolume * musicSlotB.Fade;
        }

        private static void SetMusicFade(MusicSlot slot, float value)
        {
            if (slot != null)
            {
                slot.Fade = Mathf.Clamp01(value);
            }
        }

        private void StopIfSilent(MusicSlot slot)
        {
            if (slot != null && slot != activeMusicSlot && slot.Fade <= 0.001f)
            {
                ReleaseMusicSlot(slot);
            }
        }

        private void OnAppStateChanged(AppStateChangedEvent e)
        {
            worldSoundsPaused = e.Current != AppState.Playing;
            if (e.Current == AppState.Paused)
            {
                foreach (var sound in sounds.Values)
                {
                    if (sound.Source != null && !sound.UiSound && sound.Source.isPlaying)
                    {
                        sound.Source.Pause();
                        sound.PausedBySystem = true;
                    }
                }
            }
            else if (e.Previous == AppState.Paused && e.Current == AppState.Playing)
            {
                foreach (var sound in sounds.Values)
                {
                    if (sound.Source != null && sound.PausedBySystem)
                    {
                        sound.Source.UnPause();
                        sound.PausedBySystem = false;
                    }
                }
            }
        }

        private static void ReleaseMusicRequest(MusicLoadRequest request)
        {
            if (request == null)
            {
                return;
            }

            if (request.Released)
            {
                return;
            }

            if (request.HandleAssigned)
            {
                ResourceLoader.Release(request.Handle);
                request.Released = true;
            }
            else
            {
                request.ReleaseWhenAssigned = true;
            }
        }

        private void ReleaseMusicSlot(MusicSlot slot)
        {
            if (slot == null)
            {
                return;
            }

            if (slot.Source != null)
            {
                slot.Source.Stop();
                slot.Source.clip = null;
            }

            if (slot.Request != null)
            {
                ReleaseMusicRequest(slot.Request);
            }
            else if (slot.Handle.IsValid)
            {
                ResourceLoader.Release(slot.Handle);
            }

            slot.Request = null;
            slot.Handle = ResourceLoadHandle.Empty;
            slot.Address = null;
            slot.Fade = 0f;
        }

        private static void ReleaseSoundResource(PlayingSound sound)
        {
            if (sound == null)
            {
                return;
            }

            if (sound.Released)
            {
                return;
            }

            if (sound.ResourceHandleAssigned)
            {
                ResourceLoader.Release(sound.ResourceHandle);
                sound.Released = true;
            }
            else
            {
                sound.ReleaseWhenAssigned = true;
            }
        }
    }
}
