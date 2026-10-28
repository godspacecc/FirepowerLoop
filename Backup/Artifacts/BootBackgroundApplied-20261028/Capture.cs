using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>只在临时场景克隆启动界面，比较旧背景和候选背景，不保存正式场景。</summary>
public static class BootBackgroundPreview
{
    public static string Run()
    {
        if (Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying)
            throw new InvalidOperationException("仅允许本工程编辑态预览");
        var original = EditorSceneManager.OpenScene("Assets/Game/Scenes/Boot.unity", OpenSceneMode.Single);
        if (original.path != "Assets/Game/Scenes/Boot.unity" || original.isDirty) throw new InvalidOperationException("Boot状态不符合预览条件");
        var source = original.GetRootGameObjects().Single(g => g.name == "HotUpdateCanvas");
        var previousRT = RenderTexture.active;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        string output = Path.GetFullPath("../Backup/Artifacts/BootBackgroundApplied-20261028");
        try
        {
            var root = Object.Instantiate(source);
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            var camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0,0,-10);
            camera.orthographic = true; camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.cullingMask = 1 << 31;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            root.transform.Find("Panel/ProgressArea").gameObject.SetActive(true);
            root.transform.Find("Panel/ProgressArea/ProgressSlider").GetComponent<Slider>().value = .45f;
            root.transform.Find("Panel/ProgressArea/ProgressText").GetComponent<Text>().text = "45%";
            root.transform.Find("Panel/StatusText").GetComponent<Text>().text = "正在加载资源…";
            var artwork = root.transform.Find("Background/Artwork").GetComponent<RawImage>();
            var old = artwork.texture;
            var candidate = artwork.texture;
            if (!candidate) throw new InvalidOperationException("缺少已采用主菜单背景");
            foreach (bool proposed in new [] { true })
            {
                artwork.texture = proposed ? candidate : old;
                artwork.GetComponent<AspectRatioFitter>().aspectRatio = (float)artwork.texture.width / artwork.texture.height;
                foreach (var size in new [] { new Vector2Int(1600,900), new Vector2Int(1440,1080), new Vector2Int(2340,1080) })
                {
                    var rt = new RenderTexture(size.x,size.y,24);
                    var image = new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                    try
                    {
                        camera.targetTexture = rt;
                        Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)root.transform); Canvas.ForceUpdateCanvases();
                        camera.Render(); RenderTexture.active = rt;
                        image.ReadPixels(new Rect(0,0,size.x,size.y),0,0); image.Apply();
                        if (image.GetPixels32().All(p=>p.r < 3 && p.g < 3 && p.b < 3)) throw new InvalidOperationException("预览全黑");
                        File.WriteAllBytes(Path.Combine(output,"formal"+"-"+size.x+".png"),image.EncodeToPNG());
                    }
                    finally { camera.targetTexture = null; RenderTexture.active = previousRT; Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
                }
            }
            return "已生成3张磁盘正式场景克隆截图，45%为示例进度，不代表真实加载测试。";
        }
        finally { RenderTexture.active = previousRT; SceneManager.SetActiveScene(original); EditorSceneManager.CloseScene(scene,true); }
    }
}
