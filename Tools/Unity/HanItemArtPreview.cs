using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Game;
using Object=UnityEngine.Object;

/// <summary>在隔离预览场景采样正式角色和武器动作；不保存或改写用户场景。</summary>
public static class HanItemArtPreview
{
    public static string Run()
    {
        const string output="Assets/Tests/Results~/HanItems";
        Directory.CreateDirectory(output);
        var names=new[]{"HanForgedAxe","HanForgedPick","HanSteelBlade","HanBow"};
        for(int sample=0;sample<12;sample++)
        {
            int i=sample%4,pose=sample/4;var suffix=pose==0?"":pose==1?"Move":"Attack";
            var scene=EditorSceneManager.NewPreviewScene();var graph=PlayableGraph.Create("HanItemPreview");
            try
            {
                var player=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),scene);
                var presenter=player.GetComponentInChildren<CharacterWeaponPresenter>(true);
                var entry=presenter.Catalog.FindByWorldItemId(32+i);
                if(!presenter.SetWeaponId(entry.Id))throw new InvalidOperationException(presenter.LastError);
                presenter.SetManualPoseSampling(true);
                var animator=presenter.BodyAnimator;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();animator.Update(0);
                var definition=pose==0?entry.Animations.Idle:pose==1?entry.Animations.MoveForward:entry.Animations.Attack;
                var animation=AnimationClipPlayable.Create(graph,definition.Clip);
                AnimationPlayableOutput.Create(graph,"Pose",animator).SetSourcePlayable(animation);
                graph.Play();graph.Evaluate(definition.Clip.length*.4f);presenter.ApplyOffhandGrip(definition,.4f);
                var cameraObject=new GameObject("Camera",typeof(Camera));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.19f,.21f);camera.orthographic=true;camera.orthographicSize=1.25f;
                var renderers=player.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
                var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                var center=bounds.center;camera.orthographicSize=Mathf.Max(1.25f,bounds.size.magnitude*.6f);
                camera.transform.position=center+new Vector3(3,1,5);camera.transform.LookAt(center);
                var lightObject=new GameObject("Key",typeof(Light));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject,scene);
                var light=lightObject.GetComponent<Light>();light.type=LightType.Directional;light.intensity=2.5f;light.transform.rotation=Quaternion.Euler(35,160,0);
                var target=new RenderTexture(640,640,24);var texture=new Texture2D(640,640,TextureFormat.RGBA32,false);var old=RenderTexture.active;
                try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,640,640),0,0);texture.Apply();File.WriteAllBytes(output+"/Held"+names[i]+suffix+".png",texture.EncodeToPNG());}
                finally{RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);}
            }
            finally{graph.Destroy();EditorSceneManager.ClosePreviewScene(scene);}
        }
        return "Rendered twelve actual shared-player idle, movement and attack pose samples";
    }
}
