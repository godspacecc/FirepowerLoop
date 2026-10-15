using UnityEngine; using UnityEditor; using System; using System.IO; using System.Linq;
/// <summary>仅验证并渲染无人机美术 Prefab；不进入游戏、不运行测试或打包。</summary>
public static class DroneUnityVerify {
 public static string Main() {
 var path="Assets/Game/Prefabs/SceneObjects/ArcadeSupportDroneVisual.prefab";
 var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
 if(prefab==null) throw new Exception("Prefab缺失");
 var guid=AssetDatabase.AssetPathToGUID(path);
 if(guid!="849107d38a6d0b64fa25c0e3b397608f") throw new Exception("GUID发生变化");
 var utility=new PreviewRenderUtility();
 GameObject instance=null;
 try {
 instance=UnityEngine.Object.Instantiate(prefab); utility.AddSingleGO(instance);
 var renderers=instance.GetComponentsInChildren<MeshRenderer>(true);
 if(renderers.Length!=5) throw new Exception("预期5个网格");
 if(instance.GetComponentsInChildren<Collider>(true).Length!=0) throw new Exception("不得携带Collider");
 if(instance.GetComponentsInChildren<Animator>(true).Length!=0) throw new Exception("旧鸟类Animator未移除");
 if(instance.GetComponentsInChildren<MonoBehaviour>(true).Length!=0) throw new Exception("不得新增玩法组件");
 var transforms=instance.GetComponentsInChildren<Transform>(true);
 if(transforms.Any(t=>t.name=="Bird")) throw new Exception("旧鸟模型未移除");
 foreach(var suffix in new[]{"FL","FR","RL","RR"}) {
 var rotor=transforms.Single(t=>t.name=="Rotor_"+suffix);
 var propeller=transforms.Single(t=>t.name=="Propeller_"+suffix);
 if(Vector3.Distance(rotor.position,propeller.position)>.0001f) throw new Exception("旋翼轴心偏移");
 }
 var bounds=renderers[0].bounds; foreach(var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
 if(bounds.size.x<.79f || bounds.size.x>.82f || bounds.size.y>.26f) throw new Exception("模型比例或上轴不正确："+bounds);
 var triangles=0;
 foreach(var r in renderers) {
 var mesh=r.GetComponent<MeshFilter>().sharedMesh;
 if(mesh.uv.Length!=mesh.vertexCount) throw new Exception("UV缺失");
 triangles+=mesh.triangles.Length/3;
 foreach(var mat in r.sharedMaterials) {
 if(mat==null || mat.shader.name!="Universal Render Pipeline/Lit") throw new Exception("材质不正确");
 if(!AssetDatabase.GetAssetPath(mat).StartsWith("Assets/Game/Art/Arcade/SupportDrone/Materials/")) throw new Exception("材质未进入正式目录");
 }
 }
 if(triangles<3000 || triangles>4000) throw new Exception("面数超出已验证低多边形预算："+triangles);
 var body=renderers.Single(r=>r.name=="DroneBody");
 var meshBody=body.GetComponent<MeshFilter>().sharedMesh;
 var index=Array.FindIndex(body.sharedMaterials,m=>m.name=="Drone_Lens");
 var lensPoints=meshBody.GetTriangles(index).Select(i=>body.transform.TransformPoint(meshBody.vertices[i])).ToArray();
 var lensCentre=lensPoints.Aggregate(Vector3.zero,(sum,v)=>sum+v)/lensPoints.Length;
 if(lensCentre.z<=instance.transform.position.z) throw new Exception("镜头应朝向Unity +Z");
 var deps=AssetDatabase.GetDependencies(path,true);
 if(deps.Any(d=>d.StartsWith("Assets/Art/"))) throw new Exception("依赖备用资源目录");
 var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Tools/Art/ArcadeSupportDrone"));
 utility.ambientColor=new Color(.35f,.38f,.43f);
 utility.lights[0].intensity=1.3f; utility.lights[0].transform.rotation=Quaternion.Euler(45,35,0);
 utility.lights[1].intensity=.7f; utility.lights[1].transform.rotation=Quaternion.Euler(30,210,0);
 utility.camera.orthographic=true; utility.camera.orthographicSize=.62f;
 utility.camera.nearClipPlane=.01f; utility.camera.farClipPlane=10f;
 utility.camera.clearFlags=CameraClearFlags.SolidColor; utility.camera.backgroundColor=new Color(.12f,.16f,.21f,1);
 utility.camera.transform.position=bounds.center+new Vector3(1.1f,.95f,1.4f);
 utility.camera.transform.LookAt(bounds.center);
 utility.BeginStaticPreview(new Rect(0,0,1200,1000)); utility.Render(true);
 var image=utility.EndStaticPreview();
 File.WriteAllBytes(Path.Combine(folder,"unity_preview.png"),image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
 utility.camera.orthographicSize=.52f;
 utility.camera.transform.position=bounds.center+Vector3.up*2f; utility.camera.transform.rotation=Quaternion.Euler(90,0,0);
 utility.BeginStaticPreview(new Rect(0,0,1000,1000)); utility.Render(true);
 image=utility.EndStaticPreview();
 File.WriteAllBytes(Path.Combine(folder,"unity_preview_top.png"),image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
 var report="PASS\nPrefab GUID: "+guid+"\nMeshes: "+renderers.Length+"\nTriangles: "+triangles+"\nBounds(m): "+bounds.size.ToString("F4")+"\nForward: +Z\nMaterials: 4 URP Lit\nRotors: 4 independent pivots\nColliders/Animators/MonoBehaviours: 0\nBackup asset dependencies: 0\n"+string.Join("\n",deps);
 File.WriteAllText(Path.Combine(folder,"unity_verification.txt"),report);
 return report;
 } finally { utility.Cleanup(); }
 }
}
