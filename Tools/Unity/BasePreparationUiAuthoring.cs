using System;
using System.Linq;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>一次性基地准备Prefab制作入口；只供编辑器MCP调用，不参与运行时布局。</summary>
public static class BasePreparationUiAuthoring
{
    const string BoxPath="Assets/Game/Prefabs/UI/Inventory/Box/PreparationForbiddenItem.prefab";
    const string StatusPath="Assets/Game/Prefabs/UI/Inventory/Page/StatusManagement.prefab";
    const string MapPath="Assets/Game/Prefabs/UI/Expedition/MapSelection.prefab";
    static TMP_FontAsset Font=>AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Game/Fonts/Chinese/SourceHanSansCN-Normal SDF.asset");
    static GameObject Node(string name,Transform parent)
    {var go=new GameObject(name,typeof(RectTransform));if(parent!=null)go.transform.SetParent(parent,false);go.layer=parent==null?5:parent.gameObject.layer;return go;}
    static void Top(RectTransform rect,float x,float y,float width,float height,float anchor=.5f)
    {rect.anchorMin=rect.anchorMax=new Vector2(anchor,1);rect.pivot=new Vector2(anchor,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);}
    static void StretchTop(RectTransform rect,float y,float height)
    {rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-y);rect.sizeDelta=new Vector2(-20,height);}
    static TextMeshProUGUI Label(string name,Transform parent,string preview,float size=20,string key=null)
    {
        var go=Node(name,parent);var text=go.AddComponent<TextMeshProUGUI>();text.font=Font;text.fontSize=size;text.color=new Color(.88f,.9f,.86f);text.text=preview;text.alignment=TextAlignmentOptions.TopLeft;text.raycastTarget=false;
        if(key!=null){var localized=go.AddComponent<LocalizedText>();localized.Key=key;localized.Texts.Clear();text.text=preview;}
        return text;
    }
    static Button Button(string name,Transform parent,string label,string key)
    {
        var go=Node(name,parent);var image=go.AddComponent<Image>();image.color=new Color(.34f,.4f,.27f,.96f);var button=go.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
        var text=Label("Label",go.transform,label,20,key);text.alignment=TextAlignmentOptions.Center;
        var r=text.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(6,4);r.offsetMax=new Vector2(-6,-4);
        return button;
    }
    static T Ref<T>(GameObject root,string key) where T:UnityEngine.Object=>(T)root.GetComponent<ReferenceCollector>().data.Single(x=>x.key==key).gameObject;
    static void Remove(Transform root,string name){var old=root.Find(name);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);}
    public static string CreateBox()
    {
        if(AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath)!=null)return "Box already authored";
        var root=Node("PreparationForbiddenItem",null);((RectTransform)root.transform).sizeDelta=new Vector2(580,28);
        var rc=root.AddComponent<ReferenceCollector>();var text=Label("NameText",root.transform,"手枪 × 1",20);text.color=new Color(.93f,.62f,.48f);
        var rect=text.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(4,0);rect.offsetMax=new Vector2(-4,0);rc.Add("NameText",text);
        var layout=root.AddComponent<LayoutElement>();layout.preferredHeight=28;layout.minHeight=28;
        PrefabUtility.SaveAsPrefabAsset(root,BoxPath);UnityEngine.Object.DestroyImmediate(root);
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath);UIEditorHelper.SmartUpdateBoxUI();return BoxPath;
    }
    public static string CreateStatus()
    {
        var root=PrefabUtility.LoadPrefabContents(StatusPath);
        try
        {
            if(root.transform.Find("PreparationBar")==null)
            {
                var children=root.transform.Cast<Transform>().ToArray();var body=Node("BodyContent",root.transform);
                var br=(RectTransform)body.transform;br.anchorMin=Vector2.zero;br.anchorMax=Vector2.one;br.offsetMin=Vector2.zero;br.offsetMax=new Vector2(0,-92);
                foreach(var child in children)child.SetParent(body.transform,false);
                var bar=Node("PreparationBar",root.transform);StretchTop((RectTransform)bar.transform,0,84);var rc=root.GetComponent<ReferenceCollector>();rc.Add("PreparationRoot",bar);
                var date=Label("BaseDateText",bar.transform,"基地第 1 天 08:00",23);Top(date.rectTransform,8,5,330,32,0);rc.Add("BaseDateText",date);
                var gold=Label("BaseGoldText",bar.transform,"金币：80",20);Top(gold.rectTransform,8,43,300,30,0);rc.Add("BaseGoldText",gold);
                var info=Label("RestInfoText",bar.transform,"免费恢复状态\n每日准备资金：80金币",17);Top(info.rectTransform,-20,8,315,65,.55f);rc.Add("RestInfoText",info);
                var rest=Button("BtnRest",bar.transform,"休息到次日08:00","ui.base_prep.rest");Top((RectTransform)rest.transform,-4,17,222,48,1);rc.Add("BtnRest",rest);
                PrefabUtility.SaveAsPrefabAsset(root,StatusPath);
            }
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(StatusPath);UIEditorHelper.SmartUpdatePage();return StatusPath;
    }
    public static string CreateMap()
    {
        var root=PrefabUtility.LoadPrefabContents(MapPath);
        try
        {
            var rc=root.GetComponent<ReferenceCollector>();var detail=Ref<TextMeshProUGUI>(root,"StoryText").transform.parent;
            if(detail.Find("PreparationArea")==null)
            {
                StretchTop(Ref<TextMeshProUGUI>(root,"SelectedName").rectTransform,4,36);
                StretchTop(Ref<TextMeshProUGUI>(root,"EraText").rectTransform,45,28);
                var story=Ref<TextMeshProUGUI>(root,"StoryText");StretchTop(story.rectTransform,81,40);story.fontSize=18;
                StretchTop((RectTransform)Ref<Image>(root,"Preview").transform,128,120);
                StretchTop((RectTransform)Ref<GameObject>(root,"TechRoot").transform,427,62);
                var area=Node("PreparationArea",detail);var ar=(RectTransform)area.transform;ar.anchorMin=Vector2.zero;ar.anchorMax=Vector2.one;ar.offsetMin=ar.offsetMax=Vector2.zero;rc.Add("PreparationRoot",area);
                var load=Label("CarryWeightText",area.transform,"携带重量：0.0 / 20.0千克",23);StretchTop(load.rectTransform,258,34);rc.Add("CarryWeightText",load);
                var state=Label("CarryStateText",area.transform,"当前没有禁带物品",18);StretchTop(state.rectTransform,297,26);rc.Add("CarryStateText",state);
                var scrollGo=Node("ForbiddenScroll",area.transform);StretchTop((RectTransform)scrollGo.transform,329,86);
                var bg=scrollGo.AddComponent<Image>();bg.color=new Color(.07f,.1f,.1f,.55f);var scroll=scrollGo.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;
                var viewport=Node("Viewport",scrollGo.transform);var vr=(RectTransform)viewport.transform;vr.anchorMin=Vector2.zero;vr.anchorMax=Vector2.one;vr.offsetMin=vr.offsetMax=Vector2.zero;viewport.AddComponent<RectMask2D>();
                var content=Node("Content",viewport.transform);var cr=(RectTransform)content.transform;cr.anchorMin=new Vector2(0,1);cr.anchorMax=new Vector2(1,1);cr.pivot=new Vector2(.5f,1);cr.sizeDelta=Vector2.zero;
                var layout=content.AddComponent<VerticalLayoutGroup>();layout.childControlHeight=true;layout.childControlWidth=true;layout.childForceExpandHeight=false;layout.childForceExpandWidth=true;layout.spacing=2;
                content.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
                var list=content.AddComponent<CustomListView>();list.itemPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath);if(list.itemPrefab==null)throw new InvalidOperationException("缺少禁带项Box");rc.Add("ForbiddenList",list);
                scroll.viewport=vr;scroll.content=cr;rc.Add("ForbiddenScroll",scroll);
                var adjust=Button("BtnAdjustSupplies",area.transform,"调整物资","ui.base_prep.adjust");var abr=(RectTransform)adjust.transform;abr.anchorMin=abr.anchorMax=Vector2.zero;abr.pivot=Vector2.zero;abr.anchoredPosition=new Vector2(12,0);abr.sizeDelta=new Vector2(180,42);rc.Add("BtnAdjustSupplies",adjust);
                PrefabUtility.SaveAsPrefabAsset(root,MapPath);
            }
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(MapPath);UIEditorHelper.SmartUpdateUI();return MapPath;
    }
    public static string GenerateBag()
    {Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab");UIEditorHelper.SmartUpdateUI();return "Bag generated";}
    /// <summary>View编译后完成原生成器绑定阶段；Page组件重建后重新收集宿主引用，不手写View字段。</summary>
    public static string BindAll()
    {
        if(EditorApplication.isCompiling)throw new InvalidOperationException("请等待View编译完成");
        Bind(typeof(BoxUICodeSpawner),"BindGeneratedComponent",BoxPath,"Game.PreparationForbiddenItemBoxUI");
        Bind(typeof(PageCodeSpawner),"BindGeneratedComponent",StatusPath,"Game.StatusManagementPageUI");
        const string bagPath="Assets/Game/Prefabs/UI/Inventory/Bag.prefab";
        var bag=PrefabUtility.LoadPrefabContents(bagPath);
        try{var page=bag.GetComponentsInChildren<StatusManagementPageUI>(true).Single();bag.GetComponent<ReferenceCollector>().Add("StatusPage",page);PrefabUtility.SaveAsPrefabAsset(bag,bagPath);}
        finally{PrefabUtility.UnloadPrefabContents(bag);}
        GenerateBag();
        Bind(typeof(UICodeSpawner),"BindGeneratedUiComponent",bagPath,"Game.BagUI");
        Bind(typeof(UICodeSpawner),"BindGeneratedUiComponent",MapPath,"Game.MapSelectionUI");
        foreach(var key in new[]{"AnotherDay.PendingUiPrefabPath","AnotherDay.PendingUiAssemblyType","AnotherDay.EditorTools.PendingPageUiPrefabPath","AnotherDay.EditorTools.PendingPageUiAssemblyType","AnotherDay.EditorTools.PendingBoxUiPrefabPath","AnotherDay.EditorTools.PendingBoxUiAssemblyType"})EditorPrefs.DeleteKey(key);
        return "Original generators bound Box, Page, Bag and MapSelection";
    }
    static void Bind(Type generator,string method,string path,string type)
    {generator.GetMethod(method,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{path,type});}
    /// <summary>按真实预览修正画框父级与科技区高度；不替换View组件或运行时布局。</summary>
    public static string FixMapLayout()
    {
        var root=PrefabUtility.LoadPrefabContents(MapPath);
        try
        {
            var view=root.GetComponent<MapSelectionUI>();
            StretchTop((RectTransform)view.Preview.transform.parent,114,100);
            var image=(RectTransform)view.Preview.transform;image.anchorMin=Vector2.zero;image.anchorMax=Vector2.one;image.offsetMin=image.offsetMax=Vector2.zero;
            StretchTop(view.CarryWeightText.rectTransform,220,30);StretchTop(view.CarryStateText.rectTransform,252,26);
            StretchTop((RectTransform)view.ForbiddenScroll.transform,282,68);
            StretchTop((RectTransform)view.TechRoot.transform,360,86);
            foreach(var label in view.TechRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
                StretchTop(label.rectTransform,label==view.TechnologyText?30:0,label==view.TechnologyText?50:28);
            view.TechnologyText.fontSize=17;
            StretchTop(view.ResourceText.rectTransform,453,26);view.ResourceText.fontSize=16;
            var adjust=(RectTransform)view.BtnAdjustSupplies.transform;adjust.anchoredPosition=new Vector2(12,12);
            PrefabUtility.SaveAsPrefabAsset(root,MapPath);return "Map artwork parent and technology layout corrected";
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
}
