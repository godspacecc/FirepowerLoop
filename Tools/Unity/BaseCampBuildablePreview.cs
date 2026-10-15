using System;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>只渲染配置恢复后的基地和正式 UI Prefab；不保存临时对象、不修改玩家存档。</summary>
public static class BaseCampBuildablePreview
{
    public static string Render()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity", OpenSceneMode.Additive);
        var previous = RenderTexture.active;
        var target = new RenderTexture(1680, 945, 24);
        var texture = new Texture2D(1680, 945, TextureFormat.RGB24, false);
        try
        {
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapContext>(true)).Single();
            context.SetMapContentActive(true);
            var config = new LubanRuntimeConfigProvider(() => new Tables(name => new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
            if (!config.TryGetBaseCampLayout(out var layout, out var error)) throw new InvalidOperationException(error);
            foreach (var row in layout)
            {
                if (!config.TryGetTempBuilding(row.BuildingId, out var definition, out error)) throw new InvalidOperationException(error);
                var prop = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + definition.PrefabAddress + ".prefab"), context.DynamicObjectRoot);
                prop.transform.SetPositionAndRotation(row.Position, Quaternion.Euler(0, row.Yaw, 0));
                prop.transform.localScale *= row.Scale;
                if (!prop.GetComponent<TemporaryBuildingView>().SetCommitted(definition.BlocksNavigation, out error)) throw new InvalidOperationException(error);
            }
            var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene;
            camera.transform.position = new Vector3(0, 23, -24); camera.transform.LookAt(new Vector3(0,0,1));
            camera.orthographic = true; camera.orthographicSize = 16;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f,.16f,.19f);
            camera.targetTexture = target;
            var light = new GameObject("PreviewLight", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light, scene);
            light.transform.rotation = Quaternion.Euler(48,-35,0); light.GetComponent<Light>().type = LightType.Directional; light.GetComponent<Light>().intensity = 1.5f;
            var canvasObject = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
            var mobile = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"), canvas.transform).GetComponent<MobileJoystickUI>();
            mobile.ContentVisibility.alpha = 1; mobile.ContentVisibility.interactable = true;
            mobile.InventoryButton.gameObject.SetActive(true); mobile.BuildButton.gameObject.SetActive(true);
            mobile.AttackButton.gameObject.SetActive(false); mobile.InteractionButton.gameObject.SetActive(false); mobile.ReticleRoot.SetActive(false);
            var directory = "Assets/Tests/Results~/BaseDecorationResearch";
            Directory.CreateDirectory(directory);
            Capture(camera,target,texture,directory + "/camp-mobile-vertical.png");
            mobile.ContentVisibility.alpha = 0;
            var hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/TemporaryBuilding/TemporaryBuildingHud.prefab"), canvas.transform).GetComponent<TemporaryBuildingHudUI>();
            hud.TitleText.text = "建造"; hud.ModeText.text = "摆放木桌";
            hud.HintText.text = "点击地面选择位置，旋转后确认建造";
            hud.ResourceText.text = "木材 12 / 5"; hud.StatusText.text = "可放置";
            hud.ProgressRoot.SetActive(false); hud.TouchControlsRoot.SetActive(true);
            var preview = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/Camp_table_01.prefab"), context.DynamicObjectRoot);
            preview.transform.position = new Vector3(3,0,-4); preview.GetComponent<TemporaryBuildingView>().SetPreviewValidity(true);
            Capture(camera,target,texture,directory + "/camp-mobile-placement.png");
            return "Rendered two native Prefab previews; camp restored from 44 configured placements. NPCs/player omitted from editor preview.";
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); Object.DestroyImmediate(target); EditorSceneManager.CloseScene(scene,true); }
    }
    private static void Capture(Camera camera, RenderTexture target, Texture2D texture, string path)
    {
        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
        texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG());
    }
}
