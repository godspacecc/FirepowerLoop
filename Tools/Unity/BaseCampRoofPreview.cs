using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Game;

/// <summary>创建独立临时场景对比正式棚子的遮挡表现；不保存场景或修改 Prefab。</summary>
public static class BaseCampRoofPreview
{
    public static string Render()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture texture=null;Material floorMaterial=null;Material playerMaterial=null;Camera camera=null;
        try
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(floor,scene);
            floor.transform.localScale=new Vector3(10,.05f,8);floor.transform.position=new Vector3(0,-.1f,0);
            floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.color=new Color(.32f,.37f,.33f);
            floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            var lightObject=new GameObject("Preview light");SceneManager.MoveGameObjectToScene(lightObject,scene);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(52,-35,0);
            var cameraObject=new GameObject("Preview camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
            camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.14f,.19f,.24f);
            camera.transform.position=new Vector3(3,14,-4);camera.transform.LookAt(new Vector3(0,1,-.2f));camera.fieldOfView=40;
            texture=new RenderTexture(1200,760,24);camera.targetTexture=texture;
            var views=new CameraOccluderView[2];var points=new[]{new Vector3(-2.2f,0,-.82f),new Vector3(2.2f,0,0)};
            playerMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));playerMaterial.color=new Color(.95f,.57f,.12f);
            var kinds=new[]{"shed_wood_01","awning_standing_01a"};
            for(var i=0;i<2;i++)
            {
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/Camp_"+kinds[i]+".prefab");
                var instance=UnityEngine.Object.Instantiate(prefab);SceneManager.MoveGameObjectToScene(instance,scene);
                instance.transform.position=new Vector3(points[i].x,0,0);
                instance.GetComponent<TemporaryBuildingView>().SetCommitted(false,out var error);
                if(error!=null)throw new Exception(error);
                views[i]=instance.GetComponent<CameraOccluderView>();
                var player=GameObject.CreatePrimitive(PrimitiveType.Capsule);SceneManager.MoveGameObjectToScene(player,scene);
                player.transform.position=points[i]+Vector3.up*.9f;player.transform.localScale=new Vector3(.45f,.9f,.45f);
                player.GetComponent<Renderer>().sharedMaterial=playerMaterial;
            }
            var directory="Assets/Tests/Results~/BaseDecorationResearch";System.IO.Directory.CreateDirectory(directory);
            Save(camera,texture,directory+"/camp-roofs-before.png");
            for(var i=0;i<2;i++)views[i].Refresh(false,points[i],1f);
            Save(camera,texture,directory+"/camp-roofs-hidden.png");
            for(var i=0;i<2;i++)views[i].Refresh(true,new Vector3(100,0,100),1f);
            Save(camera,texture,directory+"/camp-obstruction-faded.png");
            return System.IO.Path.GetFullPath(directory+"/camp-roofs-hidden.png");
        }
        finally
        {
            if(camera!=null)camera.targetTexture=null;
            if(texture!=null){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
            EditorSceneManager.CloseScene(scene,true);
            if(floorMaterial!=null)UnityEngine.Object.DestroyImmediate(floorMaterial);
            if(playerMaterial!=null)UnityEngine.Object.DestroyImmediate(playerMaterial);
        }
    }
    private static void Save(Camera camera,RenderTexture target,string path)
    {
        var previous=RenderTexture.active;Texture2D image=null;
        try {camera.Render();RenderTexture.active=target;image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();System.IO.File.WriteAllBytes(path,image.EncodeToPNG());}
        finally {RenderTexture.active=previous;if(image!=null)UnityEngine.Object.DestroyImmediate(image);}
    }
}