using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>隔离渲染八组正式现场模板；仅用于美术检查，不声称验证随机地图或玩家流程。</summary>
public static class HanSceneDetailPreview
{
    public static string Run()
    {
        const string output="Assets/Tests/Results~/HanScene/Details";Directory.CreateDirectory(output);
        var kit=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab").GetComponent<ValleyGenerationKit>();
        var views=kit.GetComponentsInChildren<CivilisationPointView>(true).Where(v=>v.transform.Find("StoryScene")!=null).ToArray();
        foreach(var template in views)
        {
            var scene=EditorSceneManager.NewPreviewScene();var old=RenderTexture.active;var target=new RenderTexture(800,600,24);var texture=new Texture2D(800,600,TextureFormat.RGB24,false);Material groundMaterial=null;
            try
            {
                var root=Object.Instantiate(template.gameObject);SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=Vector3.zero;root.SetActive(true);
                var label=new SerializedObject(root.GetComponent<CivilisationPointView>()).FindProperty("nameRoot").objectReferenceValue as GameObject;if(label!=null)label.SetActive(false);
                var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);SceneManager.MoveGameObjectToScene(ground,scene);ground.transform.localScale=Vector3.one*2;
                groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));groundMaterial.SetColor("_BaseColor",new Color(.31f,.29f,.21f));ground.GetComponent<Renderer>().sharedMaterial=groundMaterial;
                var cameraObject=new GameObject("DetailCamera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.GetComponent<Camera>();camera.scene=scene;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.43f,.49f,.51f);camera.fieldOfView=48;
                var center=root.name.StartsWith("Transport")?new Vector3(-.7f,.45f,.4f):new Vector3(0,.25f,.3f);
                camera.transform.position=center+new Vector3(4,4,-5);camera.transform.LookAt(center);camera.targetTexture=target;
                var sun=new GameObject("Sun",typeof(Light));SceneManager.MoveGameObjectToScene(sun,scene);sun.transform.rotation=Quaternion.Euler(50,-35,0);sun.GetComponent<Light>().type=LightType.Directional;sun.GetComponent<Light>().intensity=2.3f;
                camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,800,600),0,0);texture.Apply();File.WriteAllBytes(output+"/"+template.name+".png",texture.EncodeToPNG());
            }
            finally{RenderTexture.active=old;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);EditorSceneManager.ClosePreviewScene(scene);if(groundMaterial!=null)Object.DestroyImmediate(groundMaterial);}
        }
        return "Rendered "+views.Length+" authored templates";
    }
}
