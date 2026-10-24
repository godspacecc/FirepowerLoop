using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>使用正式Prefab制作隔离画面；示例设置值不读取或写入玩家配置，结果不代表真机验收。</summary>
public static class SettingsVisualPreview
{
    public static string Run()
    {
        if (Application.dataPath != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying) throw new InvalidOperationException("仅本项目编辑模式");
        var folder = Path.GetFullPath("../Backup/Artifacts/SettingsVisual-20261018"); Directory.CreateDirectory(folder);
        var reports = new System.Collections.Generic.List<string>();
        foreach (var spec in new[] { ("zh-CN",1920,1080), ("en-US",1920,1080), ("zh-CN",2340,1080), ("en-US",1440,1080) })
        {
            var scene = EditorSceneManager.NewPreviewScene(); var previous = RenderTexture.active; RenderTexture target = null; Texture2D pixels = null; Camera camera = null;
            try
            {
                float scale = Mathf.Sqrt(spec.Item2 / 1920f * spec.Item3 / 1080f); var width = spec.Item2 / scale; var height = spec.Item3 / scale;
                var rootCanvas = new GameObject("SettingsPreviewCanvas",typeof(RectTransform),typeof(Canvas)); rootCanvas.SetActive(false); SceneManager.MoveGameObjectToScene(rootCanvas,scene);
                var canvas = rootCanvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; ((RectTransform)rootCanvas.transform).sizeDelta = new Vector2(width,height);
                var bg = new GameObject("ExistingStartBackground",typeof(RectTransform),typeof(Image)); bg.transform.SetParent(rootCanvas.transform,false); var bgr=(RectTransform)bg.transform;bgr.anchorMin=Vector2.zero;bgr.anchorMax=Vector2.one;bgr.offsetMin=bgr.offsetMax=Vector2.zero;
                bg.GetComponent<Image>().sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/UI/Start/ArcadeIslandInfectedBackground.png");
                var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Setting.prefab"),rootCanvas.transform,false);
                var words=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/"+spec.Item1+".json"));
                foreach(var loc in root.GetComponentsInChildren<LocalizedText>(true)){loc.enabled=false;if(words[loc.Key]!=null)loc.GetComponent<TMP_Text>().text=(string)words[loc.Key];}
                var ui=root.GetComponent<SettingUI>(); var pages=new[]{ui.SettingGeneralRoot,ui.SettingAudioRoot,ui.SettingControlsRoot,ui.SettingNetworkRoot,ui.SettingMobileRoot};foreach(var page in pages)page.SetActive(false);
                var list=ui.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>().SettingList;list.totalCount=3;
                list.AddItemRefreshListener((t,i)=>{var row=t.GetComponent<SettingItemBoxUI>();row.TextSetting.text=(string)words[new[]{"ui.settings.language","ui.settings.window_size","ui.settings.window_mode"}[i]];var value=(spec.Item1=="zh-CN"?new[]{"简体中文","1920 × 1080","窗口"}:new[]{"English","1920 × 1080","Borderless Fullscreen"})[i];row.BtnContext.gameObject.SetActive(false);row.ValueDropdown.gameObject.SetActive(true);row.ValueDropdown.ClearOptions();row.ValueDropdown.AddOptions(new System.Collections.Generic.List<string>{value});row.ValueDropdown.SetValueWithoutNotify(0);row.ValueDropdown.RefreshShownValue();});list.RefreshCells();
                var audio=ui.SettingAudioRoot.GetComponent<SettingAudioPageUI>();audio.MasterVolumeSlider.value=.8f;audio.MusicVolumeSlider.value=.55f;audio.SoundVolumeSlider.value=.9f;audio.MasterVolumeValue.text="80%";audio.MusicVolumeValue.text="55%";audio.SoundVolumeValue.text="90%";
                var controls=ui.SettingControlsRoot.GetComponent<SettingControlsPageUI>().HotKeyList;controls.totalCount=4;controls.AddItemRefreshListener((t,i)=>{var row=t.GetComponent<HotKeyItemBoxUI>();row.ActionNameText.text=(spec.Item1=="zh-CN"?new[]{"暂停 / 继续","使用技能","捐赠生命","运行日志"}:new[]{"Pause / Resume","Use field upgrade","Donate a life","Runtime log"})[i];row.BindingText.text=new[]{"Esc","Q","F","F1"}[i];row.RebindButton.GetComponentInChildren<TMP_Text>().text=(string)words["ui.settings.rebind"];});controls.RefreshCells();
                var network=ui.SettingNetworkRoot.GetComponent<SettingNetworkPageUI>();network.GameServerInput.text="127.0.0.1:31001";network.ResourceServerInput.text="https://example.com/resources";network.DiagnosticNotice.text=(string)words["ui.setting.diagnostic.notice"];network.DiagnosticPending.text=string.Format((string)words["ui.setting.diagnostic.pending"],0);network.GameServerFeedback.text=spec.Item1=="zh-CN"?"下次联机时使用此地址。":"Used the next time you connect.";network.ResourceServerFeedback.text=spec.Item1=="zh-CN"?"下次启动时用于资源更新。":"Used for updates on the next launch.";
                network.DiagnosticClearButton.GetComponentInChildren<TMP_Text>(true).text=(string)words["ui.setting.diagnostic.clear"];
                camera=new GameObject("SettingsPreviewCamera",typeof(Camera)).GetComponent<Camera>();SceneManager.MoveGameObjectToScene(camera.gameObject,scene);camera.scene=scene;camera.orthographic=true;camera.orthographicSize=height*.5f;camera.aspect=(float)spec.Item2/spec.Item3;camera.transform.position=new Vector3(0,0,-100);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;canvas.worldCamera=camera;
                target=new RenderTexture(spec.Item2,spec.Item3,24);pixels=new Texture2D(spec.Item2,spec.Item3,TextureFormat.RGB24,false);camera.targetTexture=target;rootCanvas.SetActive(true);root.SetActive(true);
                var tabs=new[]{ui.GeneralTabButton,ui.AudioTabButton,ui.ControlsTabButton,ui.NetworkTabButton};var state=typeof(SettingUICtrl).GetMethod("SetTabState",BindingFlags.Static|BindingFlags.NonPublic);
                for(int j=0;j<4;j++)
                {
                    foreach(var page in pages)page.SetActive(false);pages[j].SetActive(true);for(int k=0;k<tabs.Length;k++)state.Invoke(null,new object[]{tabs[k],k==j});
                    Canvas.ForceUpdateCanvases(); foreach(var text in root.GetComponentsInChildren<TextMeshProUGUI>())text.ForceMeshUpdate();Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,spec.Item2,spec.Item3),0,0);pixels.Apply();
                    var prefix=spec.Item1+"-"+spec.Item2+"-"+new[]{"general","audio","controls","network"}[j];File.WriteAllBytes(Path.Combine(folder,prefix+".png"),pixels.EncodeToPNG());
                    var overflow=root.GetComponentsInChildren<TextMeshProUGUI>().Where(t=>t.isTextOverflowing).Select(t=>t.name+":"+t.text).ToArray();reports.Add(prefix+" overflow="+overflow.Length+" "+string.Join(" | ",overflow));
                }
            }
            finally {RenderTexture.active=previous;if(camera!=null)camera.targetTexture=null;if(target!=null){target.Release();Object.DestroyImmediate(target);}if(pixels!=null)Object.DestroyImmediate(pixels);EditorSceneManager.ClosePreviewScene(scene);}
        }
        File.WriteAllLines(Path.Combine(folder,"render-checks.txt"),reports);return folder+"\n"+string.Join("\n",reports);
    }
}
