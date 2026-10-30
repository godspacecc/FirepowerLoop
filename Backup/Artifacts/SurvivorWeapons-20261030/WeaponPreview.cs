using System;
using System.IO;
using System.Linq;
using Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>隔离场景的武器槽与常驻无人机预览；不保存正式资源。</summary>
public static class SurvivorWeaponPreview
{
    static TMP_FontAsset font;
    static Transform canvasRoot;
    static void Label(Transform parent,string value,float x,float y,float w,float h,int size)
    {
        var go=new GameObject("PreviewLabel",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);
        var rt=(RectTransform)go.transform;rt.sizeDelta=new Vector2(w,h);rt.anchoredPosition=new Vector2(x,y);
        var text=go.GetComponent<TextMeshProUGUI>();text.font=font;text.fontSize=size;text.text=value;text.alignment=TextAlignmentOptions.Center;
    }
    static GameObject Panel(Transform parent,float x,float y,float w,float h,Color color)
    {
        var go=new GameObject("PreviewPanel",typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
        var rt=(RectTransform)go.transform;rt.sizeDelta=new Vector2(w,h);rt.anchoredPosition=new Vector2(x,y);go.GetComponent<Image>().color=color;return go;
    }
    static void Render(Camera camera,string path)
    {
        var old=RenderTexture.active;var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try{foreach(var t in canvasRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;camera.targetTexture=rt;Canvas.ForceUpdateCanvases();foreach(var t in canvasRoot.GetComponentsInChildren<TMP_Text>())t.ForceMeshUpdate();camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=old;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);}
    }
    public static string Run()
    {
        if(EditorApplication.isPlaying || !Application.dataPath.Replace('\\','/').Equals("E:/GitHub/FirepowerLoop/Client/Assets"))throw new Exception("工程或状态不符");
        var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var folder=Path.GetFullPath("../Backup/Artifacts/SurvivorWeapons-20261030");Directory.CreateDirectory(folder+"/frames");Material groundMaterial=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            font=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab").GetComponent<StartUI>().BtnNewGame.GetComponentInChildren<TMP_Text>(true).font;
            var camera=new GameObject("PreviewCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=5.5f;camera.transform.position=new Vector3(0,9,-9);camera.transform.LookAt(Vector3.zero);camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.18f,.15f);
            var light=new GameObject("PreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(50,-25,0);light.intensity=1.4f;light.cullingMask=1<<31;
            var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);plane.transform.localScale=Vector3.one*3;
            groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMaterial.SetColor("_BaseColor",new Color(.24f,.38f,.20f));plane.GetComponent<Renderer>().sharedMaterial=groundMaterial;
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var drone=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeSupportDroneVisual.prefab"));
            var playerAnimator=player.GetComponentsInChildren<Animator>(true).First(a=>a.name=="PlayerHumanoid");
            var walk=AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/Player/Animations/Mixamo/Imported/RifleWalkForward.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
            foreach(var go in new[]{player,drone})foreach(var b in go.GetComponentsInChildren<MonoBehaviour>(true))b.enabled=false;
            var bullet=GameObject.CreatePrimitive(PrimitiveType.Sphere);bullet.transform.localScale=Vector3.one*.12f;
            var canvas=new GameObject("PreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvasRoot=canvas.transform;
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
            Label(canvasRoot,"幸存者模式  ·  武器槽与无人机候选效果",0,465,1700,55,36);
            Label(canvasRoot,"正式主角与现有无人机原比例 · 临时地面/弹道演示 · 非正式接入实拍",0,410,1800,45,24);
            var bar=Panel(canvasRoot,0,-425,990,100,new Color(.04f,.07f,.09f,.85f));
            string[] names={"基础步枪  Lv.1","无人机  Lv.1","空武器槽"};for(int i=0;i<3;i++){var slot=Panel(bar.transform,(i-1)*320,0,300,80,new Color(.16f,.20f,.25f,1));Label(slot.transform,names[i],0,0,290,60,28);}
            Label(canvasRoot,"无人机持续本局 · 自动射击 · 角色只需移动",0,-335,1400,60,30);
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            for(int frame=0;frame<40;frame++)
            {
                float t=frame/10f;walk.SampleAnimation(playerAnimator.gameObject,t%walk.length);player.transform.position=new Vector3(Mathf.Sin(t*1.5f)*1.3f,0,Mathf.Cos(t*1.5f)*.6f);
                drone.transform.position=new Vector3(Mathf.Sin((t-.12f)*1.5f)*1.3f+.85f,1.6f,Mathf.Cos((t-.12f)*1.5f)*.6f+.35f);
                drone.transform.rotation=Quaternion.LookRotation(Vector3.right);bullet.transform.position=drone.transform.position+Vector3.right*(t%.45f)*8;
                Render(camera,folder+"/frames/"+frame.ToString("000")+".png");if(frame==0)Render(camera,folder+"/hud-preview.png");
            }
            var shade=Panel(canvasRoot,0,0,1920,1080,new Color(.02f,.04f,.07f,.96f));
            Label(shade.transform,"等级提升",0,300,1000,70,48);Label(shade.transform,"选择一项强化",0,225,1000,50,28);
            var titles=new[]{"提升无人机等级","提升基础步枪等级","轻盈步伐"};var descriptions=new[]{"Lv.1 → Lv.2\n伤害 12 → 18\n射击间隔 0.45 → 0.40 秒","Lv.1 → Lv.2\n伤害 36 → 44\n射击间隔 0.18 → 0.17 秒","Lv.0 → Lv.1\n移动速度 +8%"};
            var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab").GetComponent<StartUI>().BtnNewGame;
            for(int i=0;i<3;i++){var card=Panel(shade.transform,(i-1)*470,-30,420,390,new Color(.16f,.20f,.25f,1));Label(card.transform,titles[i],0,120,400,65,32);Label(card.transform,descriptions[i],0,0,400,140,26);var b=Object.Instantiate(template,card.transform,false);b.gameObject.SetActive(true);foreach(var loc in b.GetComponentsInChildren<LocalizedText>(true))loc.enabled=false;var br=(RectTransform)b.transform;br.anchorMin=br.anchorMax=new Vector2(.5f,.5f);br.sizeDelta=new Vector2(300,70);br.anchoredPosition=new Vector2(0,-120);b.GetComponentInChildren<TMP_Text>(true).text="选择";}
            Label(shade.transform,"战斗已暂停",0,-330,1000,50,28);Render(camera,folder+"/upgrade-preview.png");
            return folder;
        }
        finally{SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);if(groundMaterial!=null)Object.DestroyImmediate(groundMaterial);}
    }
}
