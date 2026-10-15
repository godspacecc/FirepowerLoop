using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

/// <summary>通过真实测试指令渲染整套造型在背包和装备页的效果；隔离临时场景，不保存用户场景。</summary>
public static class PlayerAppearanceCapture
{
    public static string Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("仅用于编辑模式预览");
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var types=AppDomain.CurrentDomain.GetAssemblies();
        var isolation=types.Select(a=>a.GetType("EditModeSceneIsolation")).First(t=>t!=null);
        var type=types.Select(a=>a.GetType("Game.Tests.EquipmentManagementUiTests")).First(t=>t!=null);
        var fixture=Activator.CreateInstance(type);
        isolation.GetMethod("Enter").Invoke(null,null);
        try
        {
            type.GetMethod("SetUp").Invoke(fixture,null);
            ResourceLoader.Use(new PreviewAssetLoader(ResourceLoader.Current));
            var ui=(BagUI)type.GetField("bagUI",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(fixture);
            var host=UIHelper.GetUICtrl<BagUICtrl>();
            var canvas=ui.GetComponentInParent<Canvas>();
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;
            var camera=new GameObject("InventoryCaptureCamera",typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.10f,.11f);camera.transform.position=new Vector3(0,0,-10);
            camera.cullingMask=~(1<<31);camera.orthographic=true;camera.orthographicSize=5;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            ui.GetComponent<SafeAreaFitter>().enabled=false;
            Game.Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            var bag=Game.Game.Interface.GetModel<PlayerBagModel>().Container;
            foreach(var id in new[]{39,41,42,43,44,45,46})
            {
                bag.TryAdd(id,1,1,out _,out _);var pair=bag.Slots.Single(p=>p.Value.ItemId==id);
                Game.Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,pair.Key));
                ui.FullBtnUse.onClick.Invoke();
            }
            foreach(var id in new[]{38,40,ItemIds.Wood,ItemIds.Cloth})bag.TryAdd(id,1,20,out _,out _);
            Game.Game.Interface.SendEvent(new InventoryChangedEvent());
            Game.Game.Interface.GetUtility<IItemTransferPort>().ClickSlot(new InventorySlotRef(InventoryContainerKind.PlayerBag,0));
            var folder=Path.GetFullPath("../Tools/Unity/PlayerAppearanceCandidates/Applied");Directory.CreateDirectory(folder);
            foreach(var index in new[]{8,3,0})
            {
                var result=Game.Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync(index==0?"#appearance reset":"#appearance "+index,System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                if(!result.Success)throw new InvalidOperationException(result.Message);
                foreach(var equipment in new[]{false,true})
                {
                    host.SelectPage(equipment?BagManagementPage.Equipment:BagManagementPage.Backpack);
                    Capture(camera,ui,folder+"/"+(equipment?"equipment-":"backpack-")+index+".png",1920,1080);
                }
            }
            return folder;
        }
        finally
        {
            try{type.GetMethod("TearDown").Invoke(fixture,null);}
            finally{isolation.GetMethod("Exit").Invoke(null,null);SceneManager.SetActiveScene(previous);if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
        }
    }
    static void Capture(Camera camera,BagUI ui,string path,int width,int height)
    {
        var target=new RenderTexture(width,height,24);var image=new Texture2D(width,height,TextureFormat.RGBA32,false);var active=RenderTexture.active;
        try
        {
            camera.targetTexture=target;Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)ui.transform);Canvas.ForceUpdateCanvases();
            camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            File.WriteAllLines(path+".txt",ui.GetComponentsInChildren<TextMeshProUGUI>().Select(t=>t.name+" overflow="+t.isTextOverflowing+" text="+t.text));
        }
        finally{camera.targetTexture=null;RenderTexture.active=active;Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
    }
    sealed class Spec
    {public string Name;public int Width,Height;public bool Equipment;public Spec(string name,int width,int height,bool equipment){Name=name;Width=width;Height=height;Equipment=equipment;}}
    sealed class PreviewAssetLoader : IResourceLoader
    {
        readonly IResourceLoader fallback;
        public PreviewAssetLoader(IResourceLoader fallback){this.fallback=fallback;}
        public T LoadAsset<T>(string address) where T:Object
        {
            if(typeof(T)==typeof(Sprite))
            {
                var name=Path.GetFileName(address);
                var path=AssetDatabase.FindAssets(name+" t:Sprite",new[]{"Assets/Game/Art"}).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p)==name);
                if(path!=null)return AssetDatabase.LoadAssetAtPath<T>(path);
            }
            return fallback.LoadAsset<T>(address);
        }
        public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:Object
        {loaded?.Invoke(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
        public void Release(ResourceLoadHandle handle){fallback.Release(handle);}
    }
}
