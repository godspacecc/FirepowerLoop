using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game
{
    /// <summary>展示资源引用诊断；扫描前保护当前编辑现场，结果仅供人工核查。</summary>
    public sealed class ResourceDiagnosticsWindow : EditorWindow
    {
        // 当前成功扫描的结果；重新扫描前清空，避免失败后误用旧记录。
        private readonly List<EditorDiagnosticIssue> _issues = new List<EditorDiagnosticIssue>();
        // 结果列表的滚动位置。
        private Vector2 _scrollPosition;
        // 区分尚未扫描、阻止扫描、扫描失败与完成状态。
        private string _statusText = "尚未扫描";

        /// <summary>打开诊断窗口，不自动扫描或修改资源。</summary>
        [MenuItem("Tools/诊断/资源诊断窗口", false, 300)]
        public static void OpenWindow()
        {
            var window = GetWindow<ResourceDiagnosticsWindow>();
            window.titleContent = new GUIContent("资源诊断");
            window.minSize = new Vector2(720f, 320f);
            window.Show();
        }

        /// <summary>只在编辑态且所有已加载场景均已保存时执行窗口扫描。</summary>
        public static IReadOnlyList<EditorDiagnosticIssue> RunScan()
        {
            string blockedReason = GetScanBlockReason();
            if (blockedReason != null)
                throw new InvalidOperationException(blockedReason);
            return MissingScriptDiagnostics.ScanProject();
        }

        /// <summary>拒绝可能丢失编辑内容的扫描，不自动保存、丢弃或停止运行。</summary>
        private static string GetScanBlockReason()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "请退出运行模式后再扫描。";
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty || string.IsNullOrWhiteSpace(scene.path))
                    return $"请先保存或自行处理未保存场景：{scene.name}。扫描会逐个打开场景。";
            }
            return null;
        }

        /// <summary>每次扫描独立更新结果，阻止或失败时不保留旧结果。</summary>
        private void ScanAndApplyResults()
        {
            _issues.Clear();
            _scrollPosition = Vector2.zero;
            string blockedReason = GetScanBlockReason();
            if (blockedReason != null)
            {
                _statusText = "扫描未执行：" + blockedReason;
                return;
            }
            try
            {
                ApplyResults(RunScan());
            }
            catch (Exception exception)
            {
                _issues.Clear();
                _statusText = "扫描失败：" + exception.Message;
                Debug.LogError($"[资源诊断] {_statusText}");
            }
        }

        /// <summary>显示固定扫描范围、执行状态及人工核查结果。</summary>
        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.HelpBox(
                "范围：Assets 下全部 Prefab（含第三方及候选资源）；已启用的构建场景和 Assets/Game/Scenes 下场景。\n" +
                "扫描会逐个打开场景并尝试恢复原场景列表，不修改或保存资源。空引用可能在运行时绑定，需人工核查。",
                MessageType.Info);
            EditorGUILayout.LabelField("状态", _statusText, EditorStyles.wordWrappedLabel);
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

        /// <summary>提供扫描与清空结果操作，不提供自动修复。</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("扫描", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                ScanAndApplyResults();
            }

            if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                _issues.Clear();
                _statusText = "已清空";
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>排序本次结果，并区分确定的脚本丢失和需人工判断的引用项。</summary>
        private void ApplyResults(IEnumerable<EditorDiagnosticIssue> issues)
        {
            _issues.Clear();
            _issues.AddRange((issues ?? Enumerable.Empty<EditorDiagnosticIssue>())
                .OrderBy(issue => issue.AssetPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.HierarchyPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.IssueType));

            int missingScripts = _issues.Count(issue => issue.IssueType == EditorDiagnosticIssueType.MissingScript);
            _statusText = _issues.Count == 0
                ? "扫描完成，本次检查未发现脚本丢失或待核查引用。"
                : $"扫描完成：脚本丢失 {missingScripts} 项，引用待核查 {_issues.Count - missingScripts} 项。";
        }

        /// <summary>空引用和空键仅标记待核查，不直接判为资源损坏。</summary>
        private static string GetIssueTypeName(EditorDiagnosticIssueType issueType)
        {
            return issueType switch
            {
                EditorDiagnosticIssueType.MissingScript => "脚本丢失",
                EditorDiagnosticIssueType.ReferenceCollectorEmptyKey => "待核查：ReferenceCollector 空键",
                EditorDiagnosticIssueType.ReferenceCollectorNullObject => "待核查：ReferenceCollector 空对象",
                EditorDiagnosticIssueType.MissingTmpFont => "待核查：TMP 字体缺失",
                EditorDiagnosticIssueType.MissingAnimatorController => "待核查：Animator Controller 缺失",
                EditorDiagnosticIssueType.MissingImageSprite => "待核查：Image Sprite 缺失",
                EditorDiagnosticIssueType.MissingSpriteRendererSprite => "待核查：SpriteRenderer Sprite 缺失",
                _ => issueType.ToString(),
            };
        }
    }
}
