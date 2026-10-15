using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using TMPro;
using UnityEngine;

namespace Game
{
    [Serializable]
    public sealed class LocalizedTextEntry
    {
        public string LanguageCode;
        public string Text;
    }

    [ExecuteAlways]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class LocalizedText : MonoBehaviour
    {
        public string Key;
        public List<LocalizedTextEntry> Texts = new List<LocalizedTextEntry>();

        private TextMeshProUGUI targetText;
        private IUnRegister languageChangedUnregister;

        private void Awake()
        {
            targetText = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            languageChangedUnregister ??= Game.Interface.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged);
            Refresh();
        }

        private void OnDisable()
        {
            languageChangedUnregister?.UnRegister();
            languageChangedUnregister = null;
        }

        public void Refresh()
        {
            targetText ??= GetComponent<TextMeshProUGUI>();
            if (!string.IsNullOrWhiteSpace(Key))
            {
#if UNITY_EDITOR
                if (!Application.isPlaying && !ResourceLoader.IsConfigured)
                {
                    return;
                }
#endif
                var localizedText = Loc.T(Key);
#if UNITY_EDITOR
                if (!Application.isPlaying && localizedText == Key)
                {
                    return;
                }
#endif
                targetText.text = localizedText;
                return;
            }

            targetText.text = GetComponentText(GetCurrentLanguageCode(), targetText.text);
        }

        public void SetComponentText(string languageCode, string text)
        {
            var language = LocalizationLanguages.FindOrDefault(languageCode);
            var entry = Texts.FirstOrDefault(item => item.LanguageCode == language.Code);
            if (entry == null)
            {
                entry = new LocalizedTextEntry { LanguageCode = language.Code };
                Texts.Add(entry);
            }

            entry.Text = text ?? string.Empty;
        }

        public string GetComponentText(string languageCode, string fallback = "")
        {
            var language = LocalizationLanguages.FindOrDefault(languageCode);
            var currentText = FindText(language.Code);
            if (!string.IsNullOrEmpty(currentText))
            {
                return currentText;
            }

            var defaultText = FindText(LocalizationLanguages.Default.Code);
            if (!string.IsNullOrEmpty(defaultText))
            {
                return defaultText;
            }

            return Texts.FirstOrDefault(entry => !string.IsNullOrEmpty(entry.Text))?.Text
                ?? fallback
                ?? string.Empty;
        }

        private string FindText(string languageCode)
        {
            return Texts.FirstOrDefault(entry => entry.LanguageCode == languageCode)?.Text;
        }

        private static string GetCurrentLanguageCode()
        {
            return Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value;
        }

        private void OnLanguageChanged(LanguageChangedEvent e)
        {
            Refresh();
        }
    }
}
