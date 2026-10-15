using System;
using System.Linq;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>为文明场景补齐世界空间名称；复用 NPC 的字体与提示结构，不修改原 Prefab。</summary>
public static class CivilisationPromptAuthoring
{
    public static string Run()
    {
        var npc = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/BaseNpcMapGuide.prefab");
        var template = (GameObject)new SerializedObject(npc.GetComponent<NpcView>()).FindProperty("nameRoot").objectReferenceValue;
        var count = 0;
        foreach (var path in new[] { "Assets/Game/Scenes/CivilisationValleyMap.unity", "Assets/Game/Scenes/BaseMap.unity" })
        {
            var existing = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            if (existing.IsValid() && existing.isLoaded && existing.isDirty) throw new InvalidOperationException("场景有未保存修改：" + path);
            var opened = !existing.IsValid() || !existing.isLoaded;
            var scene = opened ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : existing;
            try
            {
                foreach (var point in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CivilisationPointView>(true)))
                {
                    var data = new SerializedObject(point);
                    var prompt = data.FindProperty("nameRoot").objectReferenceValue as GameObject;
                    if (prompt == null)
                    {
                        prompt = UnityEngine.Object.Instantiate(template, point.transform, false);
                        prompt.name = "InteractionName";
                    }
                    prompt.SetActive(false);
                    prompt.transform.localPosition = new Vector3(0, 1.8f, 0);
                    var rect = (RectTransform)prompt.transform; rect.sizeDelta = new Vector2(360, 60);
                    var text = prompt.GetComponentInChildren<TMP_Text>(true);
                    text.text = point.DisplayName; text.raycastTarget = false; text.fontSize = 24;
                    var key = point.Kind == CivilisationPointKind.Search
                        ? "civilisation.point." + point.PointId.Substring("camp.".Length)
                        : point.Kind == CivilisationPointKind.Preparation ? "civilisation.point.prepare"
                        : point.Kind == CivilisationPointKind.Return ? "civilisation.point.return" : "civilisation.point.workshop";
                    var localized = text.GetComponent<LocalizedText>() ?? text.gameObject.AddComponent<LocalizedText>();
                    var localization = new SerializedObject(localized);
                    localization.FindProperty("Key").stringValue = key;
                    localization.FindProperty("Texts").arraySize = 0;
                    localization.ApplyModifiedPropertiesWithoutUndo();
                    data.FindProperty("nameRoot").objectReferenceValue = prompt; data.ApplyModifiedPropertiesWithoutUndo();
                    count++;
                }
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("名称提示保存失败：" + path);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        return "已保存 " + count + " 个文明交互名称提示。";
    }
}
