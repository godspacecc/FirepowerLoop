#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// 提供资源更新公告的受限编辑窗口，自动绑定版本和语言，不允许手动输入版本号。
    /// </summary>
    public sealed class ResourceUpdateNoticeEditorWindow : EditorWindow
    {
#pragma warning disable CS0649
        internal static System.Func<object> ResolveVersionBindingForTests;
        internal static System.Func<object, string, string, string, string> SaveForTests;
#pragma warning restore CS0649

        private IReadOnlyList<ResourceUpdateNoticeEditorUtility.LanguageInfo> languages;
        private ResourceUpdateNoticeEditorUtility.VersionBinding binding;
        private ResourceUpdateNoticeEditorUtility.NoticeEditState state;
        private string defaultLanguage;
        private Vector2 scroll;
        private string message;

        [MenuItem("Tools/服务器/资源更新公告", false, 340)]
        public static void Open()
        {
            GetWindow<ResourceUpdateNoticeEditorWindow>("资源更新公告");
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += Reload;
            Reload();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Reload;
        }

        private static ResourceUpdateNoticeEditorUtility.VersionBinding ResolveCurrentBinding()
        {
            var testBinding = ResolveVersionBindingForTests?.Invoke();
            return testBinding != null
                ? (ResourceUpdateNoticeEditorUtility.VersionBinding)testBinding
                : ResourceUpdateNoticeEditorUtility.ResolveVersionBinding();
        }

        private void OnFocus()
        {
            Reload();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("重新加载", EditorStyles.toolbarButton, GUILayout.Width(72)))
            {
                Reload();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (binding == null || !binding.CanSave)
            {
                EditorGUILayout.HelpBox(binding?.Error ?? "未找到有效 Host 版本，无法保存公告。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("目标版本", binding.Version);
            EditorGUILayout.LabelField("状态", binding.Status);
            state.Enabled = EditorGUILayout.Toggle("启用", state.Enabled);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var language in languages)
            {
                DrawLanguage(language);
            }

            foreach (var inactive in state.Texts.Keys.Where(key => languages.All(item => item.Code != key)).ToArray())
            {
                EditorGUILayout.HelpBox($"已停用语言保留历史文本：{inactive}", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrWhiteSpace(message))
            {
                EditorGUILayout.HelpBox(message, MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!binding.CanSave))
            {
                if (GUILayout.Button("保存公告"))
                {
                    Save();
                }
            }
        }

        private void DrawLanguage(ResourceUpdateNoticeEditorUtility.LanguageInfo language)
        {
            if (!state.Texts.TryGetValue(language.Code, out var content))
            {
                content = new ResourceUpdateNoticeContent(binding.Version, string.Empty, string.Empty, new string[0]);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"{language.Code}  {language.Label}", EditorStyles.boldLabel);
            var title = EditorGUILayout.TextField("标题", content.Title);
            var summary = EditorGUILayout.TextField("概要", content.Summary);
            var items = (content.Items ?? new string[0]).ToList();
            var remove = -1;
            for (var i = 0; i < items.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                items[i] = EditorGUILayout.TextField($"条目 {i + 1}", items[i]);
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    remove = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (remove >= 0)
            {
                items.RemoveAt(remove);
            }

            if (items.Count < 20 && GUILayout.Button("添加条目", GUILayout.Width(96)))
            {
                items.Add(string.Empty);
            }

            state.Texts[language.Code] = new ResourceUpdateNoticeContent(binding.Version, title, summary, items);
        }

        private void Reload()
        {
            languages = ResourceUpdateNoticeEditorUtility.LoadLanguages(out defaultLanguage);
            binding = ResolveCurrentBinding();
            if (binding != null && binding.CanSave)
            {
                state = ResourceUpdateNoticeEditorUtility.LoadEditState(binding.Version, languages);
            }

            Repaint();
        }

        private void Save()
        {
            languages = ResourceUpdateNoticeEditorUtility.LoadLanguages(out defaultLanguage);
            binding = ResolveCurrentBinding();
            if (binding == null || !binding.CanSave)
            {
                message = binding?.Error ?? "当前没有可保存的资源版本。";
                Repaint();
                return;
            }

            state.Version = binding.Version;
            foreach (var language in languages)
            {
                if (!state.Texts.ContainsKey(language.Code))
                {
                    state.Texts[language.Code] = new ResourceUpdateNoticeContent(
                        binding.Version,
                        string.Empty,
                        string.Empty,
                        System.Array.Empty<string>());
                }
            }

            try
            {
                message = SaveForTests?.Invoke(state, defaultLanguage, binding.LatestBundle, binding.Status)
                    ?? ResourceUpdateNoticeEditorUtility.Save(state, defaultLanguage, binding.LatestBundle, binding.Status);
            }
            catch (System.Exception exception)
            {
                message = exception.Message;
            }

            Reload();
        }
    }
}
#endif
