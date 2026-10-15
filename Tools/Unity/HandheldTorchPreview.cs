using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Game;

/// <summary>临时实例渲染正式火把与玩家握持，不保存或修改场景，恢复全局环境光。</summary>
public static class HandheldTorchPreview
{
    public static string Run()
    {
        var stage=new GameObject("TorchPreview");stage.transform.position=new Vector3(1000,1000,1000);
        var oldAmbient=RenderSettings.ambientLight;var oldMode=RenderSettings.ambientMode;
        var oldActive=RenderTexture.active;var rt=new RenderTexture(1000,900,24);Texture2D pixels=null;
        try
        {
            var player=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),stage.transform);
            player.transform.localPosition=Vector3.zero;player.transform.localRotation=Quaternion.Euler(0,180,0);
            var animator=player.GetComponent<PlayerView>().Animator;animator.Rebind();animator.Update(0);
            var presenter=player.GetComponent<CharacterWeaponPresenter>();var entry=presenter.Catalog.FindByWorldItemId(48);
            if(!presenter.SetWeapon(entry))throw new InvalidOperationException(presenter.LastError);
            entry.Animations.Idle.Clip.SampleAnimation(animator.gameObject,.2f);
            presenter.ApplyOffhandGrip(entry.Animations.Idle,.2f);
            var fire=presenter.Visual.GetComponentInChildren<HandheldTorchView>(true);
            var effects=fire.transform.Find("燃烧效果");effects.gameObject.SetActive(true);
            foreach(var ps in effects.GetComponentsInChildren<ParticleSystem>())ps.Simulate(2f,true,true);
            var plane=GameObject.CreatePrimitive(PrimitiveType.Plane);plane.transform.SetParent(stage.transform,false);plane.transform.localScale=Vector3.one*.6f;
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",new Color(.13f,.12f,.09f));plane.GetComponent<Renderer>().sharedMaterial=mat;
            var lamp=new GameObject("PreviewMoon");lamp.transform.SetParent(stage.transform,false);lamp.transform.rotation=Quaternion.Euler(55,25,0);
            var light=lamp.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.18f;light.color=new Color(.5f,.65f,1f);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.055f,.06f,.08f);
            var camGo=new GameObject("PreviewCamera");camGo.transform.SetParent(stage.transform,false);var cam=camGo.AddComponent<Camera>();
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.035f,.043f,.06f);cam.fieldOfView=38;cam.nearClipPlane=.03f;cam.farClipPlane=15;cam.targetTexture=rt;
            cam.transform.position=stage.transform.position+new Vector3(2.8f,1.9f,-3.6f);cam.transform.LookAt(stage.transform.position+new Vector3(0,.95f,0));
            cam.Render();RenderTexture.active=rt;pixels=new Texture2D(1000,900,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,1000,900),0,0);pixels.Apply();
            var path=Path.GetFullPath("../Tools/Unity/HandheldTorchPreview.png");File.WriteAllBytes(path,pixels.EncodeToPNG());
            cam.targetTexture=null;UnityEngine.Object.DestroyImmediate(mat);return path;
        }
        finally
        {
            RenderSettings.ambientLight=oldAmbient;RenderSettings.ambientMode=oldMode;RenderTexture.active=oldActive;
            rt.Release();UnityEngine.Object.DestroyImmediate(rt);if(pixels!=null)UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(stage);
        }
    }
}
