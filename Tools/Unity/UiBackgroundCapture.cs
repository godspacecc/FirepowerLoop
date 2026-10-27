using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>隔离渲染正式界面，示例进度仅用于截图；不运行对局或修改Prefab、场景、存档。</summary>
public static class UiBackgroundCapture
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "F:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅本工程编辑态拍摄");
        string output = Path.GetFullPath("../Backup/Artifacts/UIBackgroundApplied-20261027");
        var previousScene = SceneManager.GetActiveScene(); var previousRT = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var reports = new List<object>();
        try
        {
            var camera = new GameObject("BackgroundCaptureCamera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.cullingMask = 1 << 31;
            var canvas = new GameObject("BackgroundCaptureCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.gameObject.layer = 31;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var strings = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            var ids = new[] { 1, 21, 22, 23, 24, 25, 5, 7, 10, 20 };
            foreach (var name in new[] { "Home/Start", "Online/OnlineRoom", "Common/Loading" })
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/" + name + ".prefab"), canvas.transform, false);
                try
                {
                    root.SetActive(true);
                    foreach (var fitter in root.GetComponentsInChildren<SafeAreaFitter>(true)) fitter.enabled = false;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                    foreach (var localized in root.GetComponentsInChildren<LocalizedText>(true))
                        if (strings.TryGetValue(localized.Key, out var text)) localized.GetComponent<TMP_Text>().text = text;
                    var loading = root.GetComponent<LoadingUI>();
                    if (loading != null)
                    {
                        loading.MapRoot.SetActive(true); loading.DownloadRoot.SetActive(false); loading.ErrorRoot.SetActive(false);
                        loading.BaseArtwork.SetActive(false); loading.BaseTitle.SetActive(false); loading.MapArtwork.SetActive(true); loading.MapTitle.SetActive(true);
                        loading.BtnRetry.gameObject.SetActive(false); loading.BtnCopyLog.gameObject.SetActive(false); loading.CopyStatusText.text = "";
                        loading.ProgressSlider.value = .45f; loading.PercentText.text = "45%"; loading.MessageText.text = strings["ui.loading.scene_status"];
                    }
                    var start = root.GetComponent<StartUI>();
                    if (start != null) { start.UpdateStatusText.text = ""; start.VersionText.text = ""; start.BtnContinue.gameObject.SetActive(false); }
                    var lobby = root.GetComponent<OnlineRoomUI>();
                    if (lobby != null) { lobby.EmptyRoot.SetActive(true); lobby.JoinedRoot.SetActive(false); lobby.DisconnectHintRoot.SetActive(false); }
                    int count = loading == null ? 1 : ids.Length;
                    for (int index = 0; index < count; index++)
                    {
                        if (loading != null) loading.LoadingArtwork.ShowArena(ids[index]);
                        foreach (var size in new[] { new Vector2Int(1672, 941), new Vector2Int(1440, 1080), new Vector2Int(2340, 1080) })
                        {
                            // 全部十关检查标准屏；相同布局的窄/宽屏仅拍首关，避免重复产物。
                            if (index > 0 && size.x != 1672) continue;
                            string file = (loading != null ? "loading-" + ids[index] : name.Replace('/', '-')) + "-" + size.x + ".png";
                            var rt = new RenderTexture(size.x, size.y, 24); var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                            try
                            {
                                camera.targetTexture = rt;
                                Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform); Canvas.ForceUpdateCanvases();
                                foreach (var text in root.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
                                camera.Render(); RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); texture.Apply();
                                if (texture.GetPixels32().All(pixel => pixel.r < 3 && pixel.g < 3 && pixel.b < 3))
                                    throw new InvalidOperationException("截图全黑，不能当作验证证据：" + file);
                                File.WriteAllBytes(Path.Combine(output, file), texture.EncodeToPNG());
                                reports.Add(new { file, prefab = name, arena = loading != null ? ids[index] : 0, width = size.x, height = size.y,
                                    overflowing = root.GetComponentsInChildren<TMP_Text>().Where(t => t.isTextOverflowing).Select(t => new { name = t.name, text = t.text }).ToArray() });
                            }
                            finally { camera.targetTexture = null; RenderTexture.active = previousRT; Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); }
                        }
                    }
                }
                finally { Object.DestroyImmediate(root); }
            }
            File.WriteAllText(Path.Combine(output, "capture.json"), JsonConvert.SerializeObject(reports, Formatting.Indented));
            return "Captured " + reports.Count + " formal prefab views: " + output;
        }
        finally { RenderTexture.active = previousRT; SceneManager.SetActiveScene(previousScene); EditorSceneManager.CloseScene(scene, true); }
    }
}
