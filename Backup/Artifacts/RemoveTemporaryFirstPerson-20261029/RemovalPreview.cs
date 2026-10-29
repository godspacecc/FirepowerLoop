using System; using System.IO; using System.Linq; using UnityEngine; using UnityEngine.UI; using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine.SceneManagement; using TMPro;
public static class RemovalPreview {
public static string Run() {
var dir="E:/GitHub/FirepowerLoop/Backup/Artifacts/RemoveTemporaryFirstPerson-20261029"; Directory.CreateDirectory(dir);
var scene=EditorSceneManager.NewPreviewScene(); var previous=RenderTexture.active;
try {
foreach(var after in new[]{false,true}) {
var cameraGo=new GameObject("PreviewCamera"); SceneManager.MoveGameObjectToScene(cameraGo,scene); var camera=cameraGo.AddComponent<Camera>(); camera.scene=scene; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.05f,.07f); camera.transform.position=new Vector3(0,0,-10);
var rt=new RenderTexture(1920,1080,24); camera.targetTexture=rt;
var canvasGo=new GameObject("PreviewCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)); SceneManager.MoveGameObjectToScene(canvasGo,scene);
var canvas=canvasGo.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
var scaler=canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
var instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab"),canvasGo.transform,false); instance.SetActive(true);
foreach(var f in instance.GetComponentsInChildren<Game.SafeAreaFitter>(true)) f.enabled=false;
var ui=instance.GetComponent<Game.MobileControlLayoutUI>();
ui.ExitFirstPersonPreview.gameObject.SetActive(!after); ui.FirstPersonFirePreview.gameObject.SetActive(!after); ui.AimPreview.gameObject.SetActive(after); ui.ModeButton.gameObject.SetActive(!after); ui.ModeText.text="第一人称操控";
if(after) {ui.ModeText.text="俯视操作"; ui.HintText.text="拖动控件调整位置；临时第一人称控件已移除（候选预览）";} else {ui.HintText.text="现有布局控件（正式Prefab副本，未进入实际设置流程）";}
foreach(var t in instance.GetComponentsInChildren<TextMeshProUGUI>(true)) { var loc=t.GetComponent<Game.LocalizedText>(); if(loc)loc.enabled=false; }
for(int i=0;i<3;i++){LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)instance.transform); Canvas.ForceUpdateCanvases();}
var draft=new Game.MobileControlLayoutData(); var buttons=new[]{ui.PausePreview,ui.BombPreview,ui.DashPreview,ui.FieldUpgradePreview,ui.DonateLifePreview,ui.ExitFirstPersonPreview,ui.FirstPersonFirePreview}; var obstacles=Game.MobileControlLayoutGeometry.RenderArcadeButtons(ui.LayoutArea,draft,buttons.Select(h=>(RectTransform)h.transform).ToArray(),!after); Game.MobileControlLayoutGeometry.Render(ui.LayoutArea,(RectTransform)ui.MovePreview.transform,ui.MovePreviewOpacity,draft.Move,true,obstacles); Game.MobileControlLayoutGeometry.Render(ui.LayoutArea,(RectTransform)ui.AimPreview.transform,ui.AimPreviewOpacity,draft.Aim,false,obstacles); Canvas.ForceUpdateCanvases(); foreach(var t in instance.GetComponentsInChildren<TextMeshProUGUI>()) t.ForceMeshUpdate();
camera.Render(); RenderTexture.active=rt; var image=new Texture2D(1920,1080,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,1920,1080),0,0); image.Apply(); File.WriteAllBytes(dir+(after?"/layout-after.png":"/layout-before.png"),image.EncodeToPNG());
File.WriteAllLines(dir+(after?"/layout-after-text.txt":"/layout-before-text.txt"),instance.GetComponentsInChildren<TextMeshProUGUI>().Select(t=>t.name+": "+t.text));
UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(canvasGo); camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(cameraGo); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
}
return dir;
} finally {RenderTexture.active=previous;EditorSceneManager.ClosePreviewScene(scene);}
}}
