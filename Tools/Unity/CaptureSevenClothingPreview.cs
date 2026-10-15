using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System.Linq;
using Game;

/// <summary>使用真实装备页预览与正式配置留存七部位画面；临时展示对象均释放，不写玩家数据。</summary>
public static class CaptureSevenClothingPreview
{
    public static string Run()
    {
        var target=new GameObject("SevenPartCapture",typeof(RectTransform),typeof(RawImage));
        var type=typeof(Game.Game).Assembly.GetType("Game.ClothingPreviewPresenter");
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/OutfitPrototype/ClothingCharacterPreview.prefab");
        var presenter=(IDisposable)Activator.CreateInstance(type,new object[]{prefab,target.GetComponent<RawImage>()});
        var folder=Path.GetFullPath("../Tools/Blender/OutfitPreview/SevenParts");Directory.CreateDirectory(folder);
        var config=new LubanRuntimeConfigProvider(()=>new global::Tables(name=>new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/"+name+".bytes"))));
        try
        {
            var root=(GameObject)type.GetField("root",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(presenter);
            var actor=root.GetComponentInChildren<Animator>();
            var camera=(Camera)type.GetField("camera",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(presenter);
            int[][] outfits={new[]{39,41,0,0,0,0,0},new[]{39,41,42,43,44,45,46},new[]{38,40,42,43,44,45,46}};
            for(int i=0;i<8;i++)
            {
                actor.transform.localRotation=Quaternion.Euler(0,i==3?168:i==4?90:-12,0);
                bool walk=i>=5;actor.SetFloat("MoveSpeed",walk?3f:0);actor.Update(walk?.32f:.1f);
                int outfit=i==0?0:i==2?2:1;
                type.GetMethod("RefreshItems").Invoke(presenter,new object[]{config,outfits[outfit]});
                Save(target.GetComponent<RawImage>().texture as RenderTexture,Path.Combine(folder,"SevenParts_"+i+".png"));
            }
            // 手动采样项目实际 Humanoid 动画，避免预览状态机尚未过渡时误把站姿当作行走验收。
            actor.runtimeAnimatorController=null;actor.applyRootMotion=false;
            foreach(var mode in new[]{"Walk","Run"})
            {
                var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Art/Player/Animations/Mixamo/Imported/Standard"+mode+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
                var graph=PlayableGraph.Create("SevenPartsMotion");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                try
                {
                    var playable=AnimationClipPlayable.Create(graph,clip);AnimationPlayableOutput.Create(graph,"Pose",actor).SetSourcePlayable(playable);graph.Play();
                    for(int frame=0;frame<4;frame++)
                    {
                        playable.SetTime(clip.length*(frame+.25)/4);graph.Evaluate(.0001f);
                        actor.transform.localPosition=Vector3.zero;actor.transform.localRotation=Quaternion.Euler(0,-12,0);
                        type.GetMethod("RefreshItems").Invoke(presenter,new object[]{config,outfits[1]});
                        Save(target.GetComponent<RawImage>().texture as RenderTexture,Path.Combine(folder,"SevenParts_"+mode+"_"+frame+".png"));
                    }
                }
                finally{graph.Destroy();}
            }
            CaptureWeapons(folder);
            return folder;
        }
        finally{presenter.Dispose();UnityEngine.Object.DestroyImmediate(target);}
    }
    static void CaptureWeapons(string folder)
    {
        for(int sample=0;sample<4;sample++)
        {
            var scene=EditorSceneManager.NewPreviewScene();var graph=PlayableGraph.Create("SevenPartsHeldPose");
            try
            {
                var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),scene);
                root.GetComponent<PlayerView>().ClothingAppearance.Apply("Top_OliveUtility","Pants_TanCargo","Head_OliveCap","Hands_WorkGloves","Feet_BrownBoots","Back_CanvasPack","Armor_UtilityVest");
                foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))skin.forceMatrixRecalculationPerRender=true;
                var presenter=root.GetComponentInChildren<CharacterWeaponPresenter>(true);var entry=presenter.Catalog.FindByWorldItemId(34+sample%2);
                if(!presenter.SetWeaponId(entry.Id))throw new InvalidOperationException(presenter.LastError);
                presenter.SetManualPoseSampling(true);var animation=sample<2?entry.Animations.MoveForward:entry.Animations.Attack;
                var animator=presenter.BodyAnimator;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;animator.Rebind();animator.Update(0);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationPlayableOutput.Create(graph,"Pose",presenter.BodyAnimator).SetSourcePlayable(AnimationClipPlayable.Create(graph,animation.Clip));
                graph.Play();graph.Evaluate(animation.Clip.length*.4f);presenter.ApplyOffhandGrip(animation);
                var camera=new GameObject("Camera",typeof(Camera)).GetComponent<Camera>();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject,scene);
                camera.enabled=false;camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.16f);camera.orthographic=true;camera.orthographicSize=1.18f;
                camera.transform.position=new Vector3(2,1.3f,4);camera.transform.LookAt(new Vector3(0,1,0));
                var light=new GameObject("Key",typeof(Light)).GetComponent<Light>();UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject,scene);
                light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(35,160,0);
                var texture=new RenderTexture(512,768,24);
                try{camera.targetTexture=texture;camera.Render();Save(texture,Path.Combine(folder,"SevenParts_Weapon_"+sample+".png"));}
                finally{camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
            }
            finally{graph.Destroy();EditorSceneManager.ClosePreviewScene(scene);}
        }
    }
    static void Save(RenderTexture texture,string path)
    {
        var previous=RenderTexture.active;RenderTexture.active=texture;var png=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
        try{png.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());}
        finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(png);}
    }
}
