using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Game;
using Luban;
using TMPro;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>独立预览场景检查真实宝藏物品表现和 HUD 文本布局，不运行游戏或修改存档。</summary>
public static class ArcadeTreasurePreview
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("宝藏预览仅用于编辑模式");
        var prior = SceneManager.GetActiveScene(); var priorTexture = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var view = new ArcadeCombatViewController();
        var folder = "Assets/Tests/Results~/ArcadeTreasure-20261011"; Directory.CreateDirectory(folder);
        try
        {
            var tables = new Tables(name => new ByteBuf(File.ReadAllBytes("Assets/Game/Config/" + name + ".bytes")));
            var p = AnotherDay.Arcade.ArcadeTreasureRoomParameters.FromConfig(tables.TbArcadeTreasureRoom.Get("arcade.treasure.first"));
            var root = new GameObject("TreasurePreview");
            typeof(ArcadeCombatViewController).GetField("root", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, root);
            view.SetTreasureRoom(p, true, true, true, p.Rewards.Select(r => r.Id).ToArray());
            var cameraRoot = new GameObject("TreasurePreviewCamera", typeof(Camera)); var camera = cameraRoot.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 22, -10); camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true; camera.orthographicSize = 9; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .1f, .13f);
            var canvasRoot = new GameObject("HudPreview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = canvasRoot.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), canvasRoot.transform, false);
            foreach (var fitter in hud.GetComponentsInChildren<SafeAreaFitter>(true)) fitter.enabled = false;
            var ui = hud.GetComponent<ArcadeHudUI>(); ui.TextRespawn.text = "";
            foreach (var spec in new[] { ("zh-CN", 1920, 1080), ("en-US", 1920, 1080), ("zh-CN", 1080, 1920) })
            {
                var text = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("Assets/Game/Config/Localization/" + spec.Item1 + ".json"));
                ui.TextArena.text = string.Format(text["ui.arcade.hud.arena"], 1) + "\n" + string.Format(text["ui.arcade.hud.score"], 200000, 9);
                ui.TextWave.text = string.Format(text["ui.arcade.hud.treasure"], 60);
                ui.TextTemporary.text = ""; ui.TextSupport.text = "";
                ui.BtnBomb.GetComponentInChildren<TextMeshProUGUI>().text = string.Format(text["ui.arcade.bomb_count"], 9);
                ui.BtnDash.GetComponentInChildren<TextMeshProUGUI>().text = string.Format(text["ui.arcade.dash_count"], 9);
                var target = new RenderTexture(spec.Item2, spec.Item3, 24); var texture = new Texture2D(spec.Item2, spec.Item3, TextureFormat.RGBA32, false);
                try
                {
                    camera.targetTexture = target; Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)hud.transform); Canvas.ForceUpdateCanvases();
                    foreach (var t in hud.GetComponentsInChildren<TextMeshProUGUI>()) t.ForceMeshUpdate();
                    camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, spec.Item2, spec.Item3), 0, 0); texture.Apply();
                    string name = "treasure-" + spec.Item1 + "-" + spec.Item2;
                    File.WriteAllBytes(folder + "/" + name + ".png", texture.EncodeToPNG());
                    File.WriteAllLines(folder + "/" + name + ".txt", new[] { ui.TextArena, ui.TextWave }.Select(t => t.name + " overflow=" + t.isTextOverflowing + " size=" + t.fontSize + " text=" + t.text));
                }
                finally { camera.targetTexture = null; RenderTexture.active = priorTexture; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); }
            }
            return Path.GetFullPath(folder);
        }
        finally { view.Dispose(); RenderTexture.active = priorTexture; SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true); }
    }
}
