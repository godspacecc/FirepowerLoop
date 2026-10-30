using System; using System.IO; using System.Collections.Generic; using UnityEngine; using UnityEditor; using UnityEditor.SceneManagement; using Object=UnityEngine.Object;
public static class PortalPreview {
public static string Run(){
 var scene=EditorSceneManager.NewPreviewScene(); var old=RenderTexture.active; var owned=new List<Object>(); var rt=new RenderTexture(960,600,24); var tex=new Texture2D(960,600,TextureFormat.RGB24,false); owned.Add(rt);owned.Add(tex);
 string dir="E:/GitHub/FirepowerLoop/Backup/Artifacts/PortalGroundPreview-20261030";Directory.CreateDirectory(dir+"/frames");
 var player=GameObject.Find("GrayboxPlayer(Clone)");if(player==null)throw new Exception("Missing live formal player");
 try{
 var stage=new GameObject("TemporaryPortalPreview");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stage,scene);
 var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(stage.transform,false);floor.transform.localPosition=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(18,.15f,15);Object.DestroyImmediate(floor.GetComponent<Collider>());
 var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mat.color=new Color(.19f,.24f,.19f);owned.Add(mat);floor.GetComponent<Renderer>().sharedMaterial=mat;
 var portals=new GameObject[2];
 for(int side=0;side<2;side++){
 float x=side==0?-3.3f:3.3f;
 portals[side]=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeStagePortalVisual.prefab"),stage.transform);portals[side].transform.localPosition=new Vector3(x,0,0);
 if(side==1){var door=portals[side].transform.Find("Portal");door.localPosition=new Vector3(0,.09f,0);door.localRotation=Quaternion.Euler(90,0,0);}
 foreach(var r in player.GetComponentsInChildren<Renderer>()){
 if(!r.enabled||!r.gameObject.activeInHierarchy)continue;
 Mesh mesh=null;var skin=r as SkinnedMeshRenderer;
 if(skin!=null){mesh=new Mesh();skin.BakeMesh(mesh);owned.Add(mesh);}else{var mf=r.GetComponent<MeshFilter>();if(mf!=null)mesh=mf.sharedMesh;}
 if(mesh==null)continue;
 var copy=new GameObject("FormalPlayerMesh",typeof(MeshFilter),typeof(MeshRenderer));copy.transform.SetParent(stage.transform,false);copy.transform.position=r.transform.position-player.transform.position+new Vector3(x-1.9f,0,-.3f);copy.transform.rotation=r.transform.rotation;copy.transform.localScale=r.transform.lossyScale;copy.GetComponent<MeshFilter>().sharedMesh=mesh;copy.GetComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
 }
 }
 foreach(var t in stage.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
 var lamp=new GameObject("PreviewLight",typeof(Light));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lamp,scene);var light=lamp.GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.cullingMask=1<<30;lamp.transform.rotation=Quaternion.Euler(50,-30,0);
 var camobj=new GameObject("PreviewCamera",typeof(Camera));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camobj,scene);var camera=camobj.GetComponent<Camera>();camera.scene=scene;camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=4.2f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.12f,.1f);camera.transform.position=new Vector3(0,11,-7);camera.transform.LookAt(new Vector3(0,.3f,0));camera.targetTexture=rt;
 for(int i=0;i<48;i++){float seconds=i/12f;foreach(var portal in portals)portal.GetComponent<Game.ArcadeStagePortalVisual>().Apply(Mathf.Max(0,seconds-.3f),1.2f);camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,960,600),0,0);tex.Apply();File.WriteAllBytes(dir+"/frames/"+i.ToString("D3")+".png",tex.EncodeToPNG());}
 camera.targetTexture=null;return dir;
 }finally{RenderTexture.active=old;EditorSceneManager.ClosePreviewScene(scene);foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);}
}}
