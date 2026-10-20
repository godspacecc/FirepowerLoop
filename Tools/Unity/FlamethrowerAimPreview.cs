using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>真实角色、喷火器与正式Animator的前后对照；仅创建临时预览场景，不修改资源或启动对局。</summary>
public static class FlamethrowerAimPreview
{
    public static string Run()
    {
        if(Application.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("仅本工程编辑态预览");
        var scene=EditorSceneManager.NewPreviewScene();var owned=new List<Object>();var oldTarget=RenderTexture.active;
        RenderTexture target=null;Texture2D pixels=null;
        GameObject New(string name){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
        Material Material(Color color){var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.color=color;owned.Add(m);return m;}
        var report=new System.Text.StringBuilder();
        try
        {
            var white=Material(new Color(.83f,.87f,.84f));var red=Material(new Color(1,.25f,.17f));var green=Material(new Color(.25f,1,.45f));
            void Line(string name,Vector3 from,Vector3 to,Material material,float width=.035f)
            {
                var line=New(name).AddComponent<LineRenderer>();line.sharedMaterial=material;line.startWidth=line.endWidth=width;
                line.positionCount=2;line.SetPositions(new[]{from,to});
            }
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.position=new Vector3(0,-.09f,1);floor.transform.localScale=new Vector3(18,.15f,16);
            floor.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/ForestGround.mat");
            var light=New("PreviewKey").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(50,-30,0);
            var camera=New("PreviewCamera").AddComponent<Camera>();camera.scene=scene;camera.orthographic=true;camera.orthographicSize=3.5f;
            camera.transform.position=new Vector3(0,8,-5);camera.transform.LookAt(new Vector3(0,.7f,.7f));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.13f);camera.cullingMask=~(1<<5);
            for(int variant=0;variant<2;variant++)
            {
                var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
                SceneManager.MoveGameObjectToScene(actor,scene);actor.transform.position=new Vector3(variant==0?-2.2f:2.2f,0,0);actor.transform.rotation=Quaternion.identity;
                var presenter=actor.GetComponent<CharacterWeaponPresenter>();actor.GetComponent<PlayerView>().SetSharedWeaponAnimationActive(true);
                if(!presenter.SetWeaponId("arcade.weapon.flamethrower"))throw new Exception(presenter.LastError);
                presenter.BodyAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(.05f);
                if(variant==0)
                {
                    // 原运行态也是这组资源姿态；只跳过新增的运行态主手校正，保留相同Animator骨骼。
                    presenter.SetManualPoseSampling(true);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
                }
                else presenter.ApplyOffhandGrip();
                foreach(var canvas in actor.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
                presenter.TryGetProjectileOrigin(out var muzzle);
                var dir=presenter.Visual.transform.forward;var floorStart=actor.transform.position+new Vector3(.7f,.025f,0);
                Line("CharacterAim",floorStart,floorStart+Vector3.forward*2.6f,white);
                Line("ArrowLeft",floorStart+Vector3.forward*2.6f,floorStart+new Vector3(-.16f,0,2.3f),white);
                Line("ArrowRight",floorStart+Vector3.forward*2.6f,floorStart+new Vector3(.16f,0,2.3f),white);
                Line("NozzleDirection",muzzle,muzzle+dir*1.5f,variant==0?red:green,.055f);
                report.AppendLine((variant==0?"Before-equivalent: ":"After: ")+"horizontal angle="+Vector3.SignedAngle(actor.transform.forward,Vector3.ProjectOnPlane(dir,actor.transform.up),actor.transform.up));
            }
            void Label(string text,float x,float y,int size)
            {
                var t=New("Caption").AddComponent<TextMesh>();t.text=text;t.anchor=TextAnchor.MiddleCenter;t.alignment=TextAlignment.Center;
                t.fontSize=size;t.characterSize=.047f;t.color=Color.white;t.transform.position=camera.ViewportToWorldPoint(new Vector3(x,y,10));t.transform.rotation=camera.transform.rotation;
            }
            Label("BEFORE: LEFT BIAS",.27f,.92f,39);Label("AFTER: AIM ALIGNED",.73f,.92f,39);
            Label("WHITE: CHARACTER AIM     |     RED / GREEN: NOZZLE DIRECTION",.5f,.07f,27);
            target=new RenderTexture(1120,700,24);pixels=new Texture2D(1120,700,TextureFormat.RGB24,false);camera.targetTexture=target;
            camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1120,700),0,0);pixels.Apply();camera.targetTexture=null;
            string folder=Path.GetFullPath("../Docs/Plans/Artifacts/FlamethrowerAimFix-20261020");Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,"comparison.png"),pixels.EncodeToPNG());File.WriteAllText(Path.Combine(folder,"probe.txt"),report.ToString());
            return folder+"\n"+report;
        }
        finally
        {
            RenderTexture.active=oldTarget;EditorSceneManager.ClosePreviewScene(scene);
            if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);
            foreach(var asset in owned)if(asset!=null)Object.DestroyImmediate(asset);
        }
    }
}
