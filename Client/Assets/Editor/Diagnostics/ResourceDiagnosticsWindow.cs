using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game
{
    public sealed class ResourceDiagnosticsWindow : EditorWindow
    {
        private readonly List<EditorDiagnosticIssue> _issues = new List<EditorDiagnosticIssue>();
        private Vector2 _scrollPosition;
        private string _statusText = "尚未扫描";

        [MenuItem("Tools/诊断/资源诊断窗口", false, 300)]
        public static void OpenWindow()
        {
            var window = GetWindow<ResourceDiagnosticsWindow>();
            window.titleContent = new GUIContent("资源诊断");
            window.minSize = new Vector2(720f, 320f);
            window.Show();
        }

        public static IReadOnlyList<EditorDiagnosticIssue> RunScan()
        {
            return MissingScriptDiagnostics.ScanProject();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.LabelField("状态", _statusText);
            EditorGUILayout.Space();

            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有诊断结果。", MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            foreach (EditorDiagnosticIssue issue in _issues)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(GetIssueTypeName(issue.IssueType), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("资源", issue.AssetPath);
                EditorGUILayout.LabelField("对象", issue.HierarchyPath);
                EditorGUILayout.LabelField("说明", issue.Message);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("扫描", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                ApplyResults(RunScan());
            }

            if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                _issues.Clear();
                _statusText = "已清空";
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyResults(IEnumerable<EditorDiagnosticIssue> issues)
        {
            _issues.Clear();
            _issues.AddRange((issues ?? Enumerable.Empty<EditorDiagnosticIssue>())
                .OrderBy(issue => issue.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.HierarchyPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.IssueType));

            _statusText = _issues.Count == 0
                ? "扫描完成，未发现问题。"
                : $"扫描完成，发现 {_issues.Count} 个问题。";
        }

        private static string GetIssueTypeName(EditorDiagnosticIssueType issueType)
        {
            return issueType switch
            {
                EditorDiagnosticIssueType.MissingScript => "脚本丢失",
                EditorDiagnosticIssueType.ReferenceCollectorEmptyKey => "ReferenceCollector 空键",
                EditorDiagnosticIssueType.ReferenceCollectorNullObject => "ReferenceCollector 空对象",
                EditorDiagnosticIssueType.MissingTmpFont => "TMP 字体缺失",
                EditorDiagnosticIssueType.MissingAnimatorController => "Animator Controller 缺失",
                EditorDiagnosticIssueType.MissingImageSprite => "Image Sprite 缺失",
                EditorDiagnosticIssueType.MissingSpriteRendererSprite => "SpriteRenderer Sprite 缺失",
                _ => issueType.ToString(),
            };
        }
    }
}
