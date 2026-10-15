using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 表示持久化用户设置根对象，读取后会清洗默认值并克隆用于预览、保存和回滚隔离。
    /// </summary>
    [Serializable]
    public sealed class SettingsData
    {
        public const int CurrentVersion = 1;
        private const string LegacyCloseTopHotKeyActionId = "ui.close-top";
        private const string LegacyToggleTestCommandActionId = "test-command.toggle";
        private static readonly HashSet<string> RemovedHotKeyActionIds = new HashSet<string>
        {
            LegacyCloseTopHotKeyActionId,
            LegacyToggleTestCommandActionId,
            // 清洗旧设置中的废弃动作，避免设置界面留下无效绑定或占用按键。
            "quick-slot.1", "quick-slot.2", "quick-slot.3",
            "quick-slot.4", "quick-slot.5", "quick-slot.6"
        };

        [JsonProperty("version")]
        public int Version { get; set; } = CurrentVersion;

        [JsonProperty("languageCode")]
        public string LanguageCode { get; set; } = "zh-CN";

        [JsonProperty("display")]
        public DisplaySettingsData Display { get; set; } = DisplaySettingsData.CreateDefault();

        [JsonProperty("audio")]
        public AudioSettingsData Audio { get; set; } = AudioSettingsData.CreateDefault();

        [JsonProperty("hotKeys")]
        public List<HotKeyBinding> HotKeys { get; set; } = HotKeyDefaults.Create();

        [JsonProperty("testTools")]
        public TestToolSettingsData TestTools { get; set; } = TestToolSettingsData.CreateDefault();

        [JsonProperty("mobileControlLayout")]
        public MobileControlLayoutData MobileControlLayout { get; set; } = new MobileControlLayoutData();

        public static SettingsData CreateDefault()
        {
            return new SettingsData().Sanitize();
        }

        public SettingsData Clone()
        {
            return new SettingsData
            {
                Version = Version,
                LanguageCode = LanguageCode,
                Display = Display?.Clone(),
                Audio = Audio?.Clone(),
                HotKeys = HotKeys?.Select(item => item?.Clone()).ToList(),
                TestTools = TestTools?.Clone(),
                MobileControlLayout = MobileControlLayout?.Clone()
            }.Sanitize();
        }

        public SettingsData Sanitize()
        {
            Version = CurrentVersion;
            LanguageCode = LocalizationLanguages.FindOrDefault(LanguageCode).Code;
            Display = (Display ?? DisplaySettingsData.CreateDefault()).Sanitize();
            Audio = (Audio ?? AudioSettingsData.CreateDefault()).Sanitize();
            HotKeys = SanitizeHotKeys(HotKeys);
            TestTools = (TestTools ?? TestToolSettingsData.CreateDefault()).Sanitize();
            MobileControlLayout = (MobileControlLayout ?? new MobileControlLayoutData()).Sanitize();
            return this;
        }

        public static List<HotKeyBinding> SanitizeHotKeys(IEnumerable<HotKeyBinding> bindings)
        {
            var result = new List<HotKeyBinding>();
            var seen = new HashSet<string>();

            if (bindings != null)
            {
                foreach (var binding in bindings)
                {
                    if (binding == null ||
                        string.IsNullOrWhiteSpace(binding.ActionId) ||
                        string.IsNullOrWhiteSpace(binding.BindingPath) ||
                        RemovedHotKeyActionIds.Contains(binding.ActionId.Trim()) ||
                        !seen.Add(binding.ActionId))
                    {
                        continue;
                    }

                    result.Add(new HotKeyBinding
                    {
                        ActionId = binding.ActionId.Trim(),
                        Context = binding.Context,
                        BindingPath = binding.BindingPath.Trim()
                    });
                }
            }

            foreach (var fallback in HotKeyDefaults.Create())
            {
                if (seen.Add(fallback.ActionId))
                {
                    if (fallback.ActionId == HotKeyActionIds.ToggleMap)
                    {
                        var candidates = new[] { HotKeyDefaults.Map, HotKeyDefaults.MapFallbackN,
                            HotKeyDefaults.MapFallbackF2, HotKeyDefaults.MapFallbackF3 };
                        fallback.BindingPath = candidates.FirstOrDefault(path => !result.Any(item =>
                            item.Context == HotKeyContext.Gameplay &&
                            string.Equals(item.BindingPath, path, StringComparison.OrdinalIgnoreCase))) ?? HotKeyDefaults.Map;
                    }
                    else if (fallback.ActionId == HotKeyActionIds.ToggleFlashlight)
                    {
                        var candidates = new[] { HotKeyDefaults.Flashlight, HotKeyDefaults.FlashlightFallbackL,
                            HotKeyDefaults.FlashlightFallbackF4 };
                        fallback.BindingPath = candidates.FirstOrDefault(path => !result.Any(item =>
                            item.Context == HotKeyContext.Gameplay &&
                            string.Equals(item.BindingPath, path, StringComparison.OrdinalIgnoreCase))) ?? HotKeyDefaults.Flashlight;
                    }
                    else if (fallback.ActionId == HotKeyActionIds.ToggleGameplayGuide)
                    {
                        // 旧设置可能已占用 F5；补入提示动作时选择空闲功能键，不抢走用户的原绑定。
                        fallback.BindingPath = Enumerable.Range(5, 8).Select(key => $"<Keyboard>/f{key}")
                            .FirstOrDefault(path => !result.Any(item =>
                                (item.Context == HotKeyContext.Gameplay || item.Context == HotKeyContext.Global) &&
                                string.Equals(item.BindingPath, path, StringComparison.OrdinalIgnoreCase))) ?? HotKeyDefaults.GameplayGuide;
                    }
                    result.Add(fallback);
                }
            }

            return result;
        }
    }

    [Serializable]
    public sealed class TestToolSettingsData
    {
        public const float DefaultTestCmdButtonPositionX = 0.09f;
        public const float DefaultTestCmdButtonPositionY = 0.11f;

        [JsonProperty("testCmdButtonPositionX")]
        public float TestCmdButtonPositionX { get; set; } = DefaultTestCmdButtonPositionX;

        [JsonProperty("testCmdButtonPositionY")]
        public float TestCmdButtonPositionY { get; set; } = DefaultTestCmdButtonPositionY;

        [JsonProperty("recentCommands")]
        public List<string> RecentCommands { get; set; } = new List<string>();

        public static TestToolSettingsData CreateDefault()
        {
            return new TestToolSettingsData();
        }

        public TestToolSettingsData Clone()
        {
            return new TestToolSettingsData
            {
                TestCmdButtonPositionX = TestCmdButtonPositionX,
                TestCmdButtonPositionY = TestCmdButtonPositionY,
                RecentCommands = RecentCommands?.ToList()
            };
        }

        public TestToolSettingsData Sanitize()
        {
            TestCmdButtonPositionX = SanitizeAxis(TestCmdButtonPositionX, DefaultTestCmdButtonPositionX);
            TestCmdButtonPositionY = SanitizeAxis(TestCmdButtonPositionY, DefaultTestCmdButtonPositionY);
            RecentCommands = SanitizeRecentCommands(RecentCommands);
            return this;
        }

        public static List<string> SanitizeRecentCommands(IEnumerable<string> commands)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (commands == null)
            {
                return result;
            }

            foreach (var command in commands)
            {
                var normalized = (command ?? string.Empty).Trim();
                if (!IsSubmittedCommand(normalized) || !seen.Add(normalized))
                {
                    continue;
                }

                result.Add(normalized);
                if (result.Count == TestToolSettingsModel.MaxRecentCommandCount)
                {
                    break;
                }
            }

            return result;
        }

        public static bool IsSubmittedCommand(string command)
        {
            if (string.IsNullOrEmpty(command) || (command[0] != '#' && command[0] != '$'))
            {
                return false;
            }

            return command.Length > 1 && !string.IsNullOrWhiteSpace(command.Substring(1));
        }

        private static float SanitizeAxis(float value, float defaultValue)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? defaultValue
                : Mathf.Clamp01(value);
        }
    }

    /// <summary>
    /// 保存持久化显示设置，缺失或异常分辨率会清洗为默认预设，窗口模式会按 Unity 支持范围归一化。
    /// </summary>
    [Serializable]
    public sealed class DisplaySettingsData
    {
        /// <summary>安卓目标帧率，单位 FPS；旧文件缺失或非法值回退到 60，只支持 30/60。</summary>
        [JsonProperty("mobileFrameRate")]
        public int MobileFrameRate { get; set; } = 60;

        [JsonProperty("resolutionCode")]
        public string ResolutionCode { get; set; } = DisplayResolutionPresets.Default.Code;

        [JsonProperty("windowMode")]
        public FullScreenMode WindowMode { get; set; } = FullScreenMode.Windowed;

        public static DisplaySettingsData CreateDefault()
        {
            return new DisplaySettingsData();
        }

        public DisplaySettingsData Clone()
        {
            return new DisplaySettingsData
            {
                ResolutionCode = ResolutionCode,
                WindowMode = WindowMode,
                MobileFrameRate = MobileFrameRate
            };
        }

        public DisplaySettingsData Sanitize()
        {
            ResolutionCode = DisplayResolutionPresets.FindOrDefault(ResolutionCode).Code;
            WindowMode = DisplaySettingsModel.SanitizeWindowMode(WindowMode);
            MobileFrameRate = MobileFrameRate == 30 ? 30 : 60;
            return this;
        }
    }

    /// <summary>
    /// 保存持久化音频设置，音量字段在清洗时限制到 0 到 1，静音状态原样保留。
    /// </summary>
    [Serializable]
    public sealed class AudioSettingsData
    {
        [JsonProperty("masterVolume")]
        public float MasterVolume { get; set; } = 1f;

        [JsonProperty("musicVolume")]
        public float MusicVolume { get; set; } = 1f;

        [JsonProperty("soundVolume")]
        public float SoundVolume { get; set; } = 1f;

        [JsonProperty("mute")]
        public bool Mute { get; set; }

        public static AudioSettingsData CreateDefault()
        {
            return new AudioSettingsData();
        }

        public AudioSettingsData Clone()
        {
            return new AudioSettingsData
            {
                MasterVolume = MasterVolume,
                MusicVolume = MusicVolume,
                SoundVolume = SoundVolume,
                Mute = Mute
            };
        }

        public AudioSettingsData Sanitize()
        {
            MasterVolume = Mathf.Clamp01(MasterVolume);
            MusicVolume = Mathf.Clamp01(MusicVolume);
            SoundVolume = Mathf.Clamp01(SoundVolume);
            return this;
        }
    }
}
