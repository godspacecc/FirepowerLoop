using System;
using System.IO;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>按已确认预览制作幸存者菜单及三选一，分步生成后等待编译回绑。</summary>
public static class SurvivorUiAuthoring
{
    const string StartPath="Assets/Game/Prefabs/UI/Home/Start.prefab";
    const string BoxPath="Assets/Game/Prefabs/UI/Survivor/Box/SurvivorUpgrade.prefab";
    const string UpgradePath="Assets/Game/Prefabs/UI/Survivor/SurvivorUpgrade.prefab";
    static void Guard() { if(EditorApplication.isPlaying || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets") throw new InvalidOperationException("工程或状态不符"); }
    public static string ApplyStart()
    {
        Guard(); var root=PrefabUtility.LoadPrefabContents(StartPath);
        try {
            var ui=root.GetComponent<StartUI>(); var rc=root.GetComponent<ReferenceCollector>();
            Fixed(ui.BtnNewGame.GetComponentInChildren<TMP_Text>(true),"ui.survivor.arcade","闯关模式");
            var existing=ui.BtnNewGame.transform.parent.Find("BtnSurvivor");
            var b=existing!=null?existing.GetComponent<Button>():Object.Instantiate(ui.BtnNewGame,ui.BtnNewGame.transform.parent,false);
            b.name="BtnSurvivor"; b.transform.SetSiblingIndex(ui.BtnNewGame.transform.GetSiblingIndex()+1);
            Fixed(b.GetComponentInChildren<TMP_Text>(true),"ui.survivor.mode","幸存者模式");
            rc.Add("BtnSurvivor",b); rc.Sort(); PrefabUtility.SaveAsPrefabAsset(root,StartPath);
        } finally { PrefabUtility.UnloadPrefabContents(root); }
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(StartPath)); return "Start 已生成，等待编译";
    }
    public static string ApplyBox()
    {
        Guard(); Directory.CreateDirectory(Path.GetDirectoryName(BoxPath));
        var template=AssetDatabase.LoadAssetAtPath<GameObject>(StartPath).GetComponent<StartUI>();
        var root=Panel("SurvivorUpgrade",null,420,390,0,0,new Color(.16f,.20f,.25f,1));
        try {
            var rc=root.AddComponent<ReferenceCollector>();
            var font=template.BtnNewGame.GetComponentInChildren<TMP_Text>(true).font;
            rc.Add("TitleText",Label(root.transform,font,"火力提升",0,120,400,65,36));
            rc.Add("DetailText",Label(root.transform,font,"提高子弹伤害",0,15,400,100,24));
            var b=Object.Instantiate(template.BtnNewGame,root.transform,false); b.name="BtnSelect"; b.gameObject.SetActive(true);
            SetRect((RectTransform)b.transform,300,70,0,-120); Fixed(b.GetComponentInChildren<TMP_Text>(true),"ui.survivor.select","选择");
            rc.Add("BtnSelect",b);rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,BoxPath);
        } finally {Object.DestroyImmediate(root);}
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath));return "Box 已生成，等待编译回绑后制作宿主";
    }
    public static string ApplyUpgrade()
    {
        Guard(); var template=AssetDatabase.LoadAssetAtPath<GameObject>(StartPath).GetComponent<StartUI>();
        var root=Panel("SurvivorUpgrade",null,0,0,0,0,new Color(.02f,.04f,.07f,.94f));
        try {
            var rect=(RectTransform)root.transform; rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var rc=root.AddComponent<ReferenceCollector>();var font=template.BtnNewGame.GetComponentInChildren<TMP_Text>(true).font;
            Fixed(Label(root.transform,font,"等级提升",0,300,1000,70,48),"ui.survivor.level_up","等级提升");
            Fixed(Label(root.transform,font,"选择一项强化",0,225,1000,50,28),"ui.survivor.choose","选择一项强化");
            Fixed(Label(root.transform,font,"战斗已暂停",0,-330,1000,50,28),"ui.survivor.paused","战斗已暂停");
            var cards=new GameObject("Cards",typeof(RectTransform),typeof(HorizontalLayoutGroup),typeof(CustomListView)); cards.transform.SetParent(root.transform,false);
            SetRect((RectTransform)cards.transform,1360,390,0,-30);
            var layout=cards.GetComponent<HorizontalLayoutGroup>();layout.spacing=50;layout.childAlignment=TextAnchor.MiddleCenter;layout.childControlWidth=false;layout.childControlHeight=false;layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;
            var list=cards.GetComponent<CustomListView>();list.itemPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath);rc.Add("Cards",list);rc.Sort();
            PrefabUtility.SaveAsPrefabAsset(root,UpgradePath);
        }finally{Object.DestroyImmediate(root);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(UpgradePath));return "升级宿主已生成，等待编译";
    }
    /// <summary>按已确认HUD制作非阻塞信息层，暂停按钮沿用街机现有资源。</summary>
    public static string ApplyHud()
    {
        Guard(); const string path="Assets/Game/Prefabs/UI/Survivor/SurvivorHud.prefab";
        var root=Panel("SurvivorHud",null,1920,1080,0,0,Color.clear);
        try {
            root.GetComponent<Image>().raycastTarget=false;
            var rc=root.AddComponent<ReferenceCollector>();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponent<ArcadeHudUI>();
            var font=source.GetComponentInChildren<TMP_Text>(true).font;
            var dark=new Color(.025f,.04f,.055f,.9f);
            Panel("HealthPanel",root.transform,470,122,-685,435,dark).GetComponent<Image>().raycastTarget=false;
            rc.Add("HealthText",Label(root.transform,font,"生命  80 / 100",-685,462,440,40,28));
            AddBar(rc,root.transform,"HealthBar",-685,417,400,16,new Color(.35f,.78f,.5f));
            Panel("TimePanel",root.transform,310,92,0,450,dark).GetComponent<Image>().raycastTarget=false;
            rc.Add("TimeText",Label(root.transform,font,"生存时间  08:42",0,450,300,60,30));
            Panel("ScorePanel",root.transform,470,92,685,450,dark).GetComponent<Image>().raycastTarget=false;
            rc.Add("ScoreText",Label(root.transform,font,"分数  12,480",685,450,440,60,30));
            Panel("ExperiencePanel",root.transform,800,92,0,-465,dark).GetComponent<Image>().raycastTarget=false;
            rc.Add("ExperienceText",Label(root.transform,font,"等级 8    经验  32 / 60",0,-446,760,40,26));
            AddBar(rc,root.transform,"ExperienceBar",0,-482,720,12,new Color(.32f,.72f,.87f));
            AddMoveJoystick(rc,root.transform,source);
            var pause=Object.Instantiate(source.BtnPause,root.transform,false);pause.name="BtnPause";pause.gameObject.SetActive(true);rc.Add("BtnPause",pause);
            var onlineLabel=pause.transform.Find("OnlineExitLabel");if(onlineLabel!=null)Object.DestroyImmediate(onlineLabel.gameObject);
            rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,path);
        }finally{Object.DestroyImmediate(root);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));return "HUD已生成，等待回绑";
    }
    /// <summary>按已确认结果构图制作结束界面；保存错误占用预览说明位置。</summary>
    public static string ApplyResult()
    {
        Guard();const string path="Assets/Game/Prefabs/UI/Survivor/SurvivorResult.prefab";
        var root=Panel("SurvivorResult",null,1920,1080,0,0,new Color(.02f,.03f,.04f,.65f));
        try {
            var r=(RectTransform)root.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            var rc=root.AddComponent<ReferenceCollector>();var source=AssetDatabase.LoadAssetAtPath<GameObject>(StartPath).GetComponent<StartUI>();
            var font=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponentInChildren<TMP_Text>(true).font;
            var panel=Panel("ResultPanel",root.transform,900,780,0,0,new Color(.025f,.04f,.055f,.9f));
            Fixed(Label(panel.transform,font,"本局结束",0,270,800,80,48),"ui.survivor.result.title","本局结束");
            Fixed(Label(panel.transform,font,"幸存者模式",0,195,800,50,26),"ui.survivor.mode","幸存者模式");
            rc.Add("TimeText",Label(panel.transform,font,"生存时间  08:42",0,70,800,70,36));
            rc.Add("ScoreText",Label(panel.transform,font,"本局分数  12,480",0,-20,800,70,36));
            rc.Add("ErrorText",Label(panel.transform,font,"",0,-335,800,60,20));
            for(int i=0;i<2;i++){
                var b=Object.Instantiate(source.BtnNewGame,panel.transform,false);b.name=i==0?"BtnRestart":"BtnMenu";b.gameObject.SetActive(true);SetRect((RectTransform)b.transform,360,90,i==0?-200:200,-210);
                Fixed(b.GetComponentInChildren<TMP_Text>(true),i==0?"ui.survivor.restart":"ui.pause.main_menu",i==0?"再来一局":"返回主菜单");rc.Add(b.name,b);
            }
            rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,path);
        }finally{Object.DestroyImmediate(root);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));return "结果已生成，等待回绑";
    }
    /// <summary>静态背景与动态填充均序列化，控制器只修改填充比例。</summary>
    static void AddBar(ReferenceCollector rc,Transform parent,string name,float x,float y,float w,float h,Color color)
    {
        var back=Panel(name+"Background",parent,w,h,x,y,new Color(.15f,.19f,.21f));back.GetComponent<Image>().raycastTarget=false;
        var fill=Panel(name,back.transform,w,h,0,0,color).GetComponent<Image>();fill.raycastTarget=false;
        var fillRect=(RectTransform)fill.transform;fillRect.anchorMin=Vector2.zero;fillRect.anchorMax=Vector2.one;fillRect.offsetMin=fillRect.offsetMax=Vector2.zero;
        var slider=back.AddComponent<Slider>();slider.transition=Selectable.Transition.None;slider.interactable=false;slider.fillRect=(RectTransform)fill.transform;slider.minValue=0;slider.maxValue=1;slider.value=1;rc.Add(name,slider);
    }
    /// <summary>复制闯关移动摇杆及其祖先布局，不复制瞄准或其它战斗控件。</summary>
    static void AddMoveJoystick(ReferenceCollector rc,Transform root,ArcadeHudUI source)
    {
        var ancestors=new System.Collections.Generic.Stack<RectTransform>();
        for(var p=source.MoveJoystickRoot.transform.parent;p!=null;p=p.parent)ancestors.Push((RectTransform)p);
        Transform target=root;RectTransform area=null;
        while(ancestors.Count>0)
        {
            var src=ancestors.Pop();var go=new GameObject(src.name+"Layout",typeof(RectTransform));go.transform.SetParent(target,false);
            var dst=(RectTransform)go.transform;dst.anchorMin=src.anchorMin;dst.anchorMax=src.anchorMax;dst.pivot=src.pivot;dst.sizeDelta=src.sizeDelta;dst.anchoredPosition3D=src.anchoredPosition3D;dst.localScale=src.localScale;dst.localRotation=src.localRotation;
            if(src==source.JoystickLayoutArea)area=dst;target=dst;
        }
        if(area==null)throw new InvalidOperationException("闯关摇杆布局区域不在移动摇杆祖先链");
        var joystick=Object.Instantiate(source.MoveJoystickRoot,target,false);joystick.SetActive(false);
        rc.Add("MoveJoystickRoot",joystick);rc.Add("MoveJoystick",joystick.GetComponentInChildren<UnityEngine.InputSystem.OnScreen.OnScreenStick>(true));rc.Add("MoveOpacity",joystick.GetComponent<CanvasGroup>());rc.Add("JoystickLayoutArea",area);
    }
    static void Fixed(TMP_Text text,string key,string value) {text.text=value;var loc=text.GetComponent<LocalizedText>()??text.gameObject.AddComponent<LocalizedText>();loc.Key=key;loc.Texts.Clear();}
    static void SetRect(RectTransform r,float w,float h,float x,float y){r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x,y);}
    static GameObject Panel(string name,Transform parent,float w,float h,float x,float y,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);SetRect((RectTransform)go.transform,w,h,x,y);go.GetComponent<Image>().color=color;return go;}
    static TMP_Text Label(Transform parent,TMP_FontAsset font,string value,float x,float y,float w,float h,int size){var go=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);SetRect((RectTransform)go.transform,w,h,x,y);var text=go.GetComponent<TextMeshProUGUI>();text.font=font;text.fontSize=size;text.text=value;text.alignment=TextAlignmentOptions.Center;text.color=Color.white;text.raycastTarget=false;return text;}
}
