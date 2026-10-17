using System;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>在原资源路径制作SlayerMart；布局、默认文字与列表来源全部保存到Prefab。</summary>
public static class SlayerMartUiAuthoring
{
    const string Host="Assets/Game/Prefabs/UI/Arcade/ArcadeShop.prefab";
    const string Box="Assets/Game/Prefabs/UI/Arcade/Box/ArcadeShopOfferBox.prefab";
    static TMP_FontAsset font;
    static Material fontMaterial;
    static readonly Color Gold=new Color(.94f,.76f,.38f);
    public static string BuildBox()
    {
        Guard();var root=PrefabUtility.LoadPrefabContents(Box);
        try
        {
            Clear(root);Place((RectTransform)root.transform,Vector2.zero,new Vector2(278,155));
            Surface(root,new Color(.09f,.14f,.18f));var rc=root.GetComponent<ReferenceCollector>();
            var select=root.GetComponent<Button>()??root.AddComponent<Button>();select.targetGraphic=root.GetComponent<Image>();rc.Add("BtnSelect",select);
            AddText(rc,root.transform,"TextName","爆炸补给",16,-10,246,40,27);
            AddText(rc,root.transform,"TextType","本人 · 每店一次",16,-52,246,34,22);
            AddText(rc,root.transform,"TextPrice","3,000",16,-95,122,40,27).color=Gold;
            AddText(rc,root.transform,"TextEffect","容量确认后购买",16,-120,246,34,22).gameObject.SetActive(false);
            AddText(rc,root.transform,"TextState","可购买",154,-98,105,34,22).alignment=TextAlignmentOptions.Right;
            var disabled=TopNode(root.transform,"DisabledRoot",Vector2.zero,new Vector2(278,155));
            Surface(disabled,new Color(.02f,.03f,.04f,.5f)).raycastTarget=false;rc.Add("DisabledRoot",disabled);disabled.SetActive(false);
            var selected=TopNode(root.transform,"SelectedRoot",Vector2.zero,new Vector2(278,155));
            var img=Surface(selected,new Color(0,0,0,0));img.raycastTarget=false;
            var outline=selected.AddComponent<Outline>();outline.effectColor=Gold;outline.effectDistance=new Vector2(3,3);
            // 使用四条实线边框，透明图像本身不能产生可见Outline。
            foreach(var edge in new[]{new Vector4(0,0,278,3),new Vector4(0,-152,278,3),new Vector4(0,0,3,155),new Vector4(275,0,3,155)})
                Surface(TopNode(selected.transform,"Border",new Vector2(edge.x,edge.y),new Vector2(edge.z,edge.w)),Gold).raycastTarget=false;
            rc.Add("SelectedRoot",selected);selected.SetActive(false);rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,Box);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(Box);
        if(!EditorApplication.ExecuteMenuItem("Assets/智能更新BoxUI"))throw new InvalidOperationException("Box生成入口未执行");
        return "独立商品卡已保存，BoxUI正式生成已触发";
    }
    public static string BuildHost()
    {
        Guard();var root=PrefabUtility.LoadPrefabContents(Host);
        try
        {
            Clear(root);Surface(root,new Color(.015f,.025f,.035f,.91f));var rc=root.GetComponent<ReferenceCollector>();
            var panel=Node(root.transform,"Panel",Vector2.zero,new Vector2(1780,980));Surface(panel,new Color(.045f,.075f,.1f,.98f));
            var title=Text(panel.transform,"Title","SLAYERMART",new Vector2(-820,442),new Vector2(900,70),44);title.color=Gold;
            rc.Add("TextDeadline",Text(panel.transform,"TextDeadline","剩余 300 秒",new Vector2(480,442),new Vector2(340,54),30));
            rc.Add("TextCash",Text(panel.transform,"TextCash","现金 0",new Vector2(-820,367),new Vector2(400,42),29));
            rc.Add("TextTokens",Text(panel.transform,"TextTokens","重抽券 0",new Vector2(-420,367),new Vector2(350,42),29));
            rc.Add("TextMembers",Text(panel.transform,"TextMembers","等待队友选择",new Vector2(-20,367),new Vector2(840,42),24));
            rc.Add("TextMessage",Text(panel.transform,"TextMessage","购买、重抽与完成购物互相独立",new Vector2(-820,-402),new Vector2(1640,36),23));
            var choice=Node(panel.transform,"ChoiceRoot",Vector2.zero,new Vector2(1640,620));rc.Add("ChoiceRoot",choice);
            Text(choice.transform,"ChoiceTitle","进入商店区域",new Vector2(-620,170),new Vector2(1240,70),42).alignment=TextAlignmentOptions.Center;
            Text(choice.transform,"ChoiceBody","收集现金，自选本局支援。双方选择一致后出发。",new Vector2(-620,50),new Vector2(1240,100),30).alignment=TextAlignmentOptions.Center;
            rc.Add("BtnEnter",Button(choice.transform,"BtnEnter","进入商店",new Vector2(-270,-120),new Vector2(380,74)));
            rc.Add("BtnSkip",Button(choice.transform,"BtnSkip","跳过",new Vector2(270,-120),new Vector2(380,74)));
            var loading=Node(panel.transform,"LoadingRoot",Vector2.zero,new Vector2(1640,620));rc.Add("LoadingRoot",loading);
            Text(loading.transform,"LoadingTitle","正在准备商店区域…",new Vector2(-620,80),new Vector2(1240,100),36).alignment=TextAlignmentOptions.Center;
            var shop=Node(panel.transform,"ShoppingRoot",Vector2.zero,new Vector2(1640,800));rc.Add("ShoppingRoot",shop);
            var gridRoot=Node(shop.transform,"OfferList",new Vector2(-820,300),new Vector2(1160,668),new Vector2(0,1));
            var grid=gridRoot.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(278,155);grid.spacing=new Vector2(16,16);
            grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=4;grid.childAlignment=TextAnchor.UpperLeft;
            var list=gridRoot.AddComponent<CustomListView>();list.itemPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Box);rc.Add("OfferList",list);
            var detail=Node(shop.transform,"Detail",new Vector2(360,300),new Vector2(460,668),new Vector2(0,1));Surface(detail,new Color(.07f,.11f,.14f));
            AddText(rc,detail.transform,"TextDetailName","选择商品",24,-38,412,80,34).color=Gold;
            AddText(rc,detail.transform,"TextDetailEffect","选择左侧商品查看价格、效果、限购与作用范围。",24,-140,412,400,26);
            var buy=Button(detail.transform,"BtnBuy","购买",new Vector2(230,-605),new Vector2(390,66));
            ((RectTransform)buy.transform).anchorMin=((RectTransform)buy.transform).anchorMax=new Vector2(0,1);rc.Add("BtnBuy",buy);
            rc.Add("BtnReroll",Button(shop.transform,"BtnReroll","重抽（消耗1券）",new Vector2(-600,-455),new Vector2(400,60)));
            rc.Add("BtnFinish",Button(shop.transform,"BtnFinish","完成购物",new Vector2(-120,-455),new Vector2(320,60)));
            rc.Add("BtnCloseMenu",Button(shop.transform,"BtnCloseMenu","关闭菜单",new Vector2(240,-455),new Vector2(300,60)));
            rc.Add("BtnRetry",Button(panel.transform,"BtnRetry","重试",new Vector2(670,-455),new Vector2(300,60)));
            choice.SetActive(true);loading.SetActive(false);shop.SetActive(false);rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,Host);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(Host);
        if(!EditorApplication.ExecuteMenuItem("Assets/智能更新UI"))throw new InvalidOperationException("宿主生成入口未执行");
        return "保留原GUID的4×4商店已保存，宿主UI正式生成已触发";
    }
    /// <summary>编译后完成正式生成器绑定；MCP同步执行期间延迟回调可能尚未处理。</summary>
    public static string FinishBindings()
    {
        Guard();var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
        typeof(BoxUICodeSpawner).GetMethod("BindGeneratedComponent",flags).Invoke(null,new object[]{Box,"Game.ArcadeShopOfferBoxUI"});
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",flags).Invoke(null,new object[]{Host,"Game.ArcadeShopUI"});
        return "Box与宿主正式生成引用已绑定";
    }
    /// <summary>只追加本功能四个HUD控件，保留原玩家行、摇杆、效果列表与所有资源引用。</summary>
    public static string BuildHud()
    {
        Guard();const string path="Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var names=new[]{"BtnSlayerMart","BtnFieldUpgrade","TextStormCharge","TextArmor"};
            var rc=root.GetComponent<ReferenceCollector>();
            foreach(var name in names){var old=root.transform.Find(name);if(old!=null)Object.DestroyImmediate(old.gameObject);}
            rc.data.RemoveAll(e=>Array.IndexOf(names,e.key)>=0);
            var shop=Button(root.transform,"BtnSlayerMart","商店",new Vector2(360,-80),new Vector2(300,96));
            var upgrade=Button(root.transform,"BtnFieldUpgrade","释放风暴",new Vector2(360,-80),new Vector2(280,96));
            var storm=Text(root.transform,"TextStormCharge","风暴 0/100",new Vector2(170,-155),new Vector2(380,42),26);
            var armor=Text(root.transform,"TextArmor","护甲板 0/0",new Vector2(170,-205),new Vector2(380,42),26);
            foreach(var control in new Component[]{shop,upgrade,storm,armor})
            {
                var rect=(RectTransform)control.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);
                control.gameObject.SetActive(false);rc.Add(control.name,control);
            }
            rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if(!EditorApplication.ExecuteMenuItem("Assets/智能更新UI"))throw new InvalidOperationException("HUD生成入口未执行");
        return "HUD商店与风暴控件已追加，普通入口默认隐藏";
    }
    /// <summary>编译后执行同一生成器的HUD引用绑定阶段。</summary>
    public static string FinishHudBindings()
    {
        Guard();typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
            .Invoke(null,new object[]{"Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab","Game.ArcadeHudUI"});
        return "HUD引用已绑定";
    }
    static void Guard()
    {
        if(EditorApplication.isPlaying||Application.dataPath!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("项目或编辑模式不匹配");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab").GetComponent<ArcadeHudUI>().TextWave;
        font=source.font;fontMaterial=source.fontSharedMaterial;
    }
    static void Clear(GameObject root)
    {
        for(int i=root.transform.childCount-1;i>=0;i--)Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        root.GetComponent<ReferenceCollector>().data.Clear();
    }
    static GameObject Node(Transform parent,string name,Vector2 position,Vector2 size,Vector2? pivot=null)
    {var node=new GameObject(name,typeof(RectTransform));node.transform.SetParent(parent,false);Place((RectTransform)node.transform,position,size,pivot);return node;}
    static void Place(RectTransform rect,Vector2 position,Vector2 size,Vector2? pivot=null)
    {rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=pivot??new Vector2(.5f,.5f);rect.anchoredPosition=position;rect.sizeDelta=size;}
    static GameObject TopNode(Transform parent,string name,Vector2 position,Vector2 size)
    {var node=Node(parent,name,position,size,new Vector2(0,1));var rect=(RectTransform)node.transform;rect.anchorMin=rect.anchorMax=new Vector2(0,1);return node;}
    static Image Surface(GameObject root,Color color)
    {var image=root.GetComponent<Image>()??root.AddComponent<Image>();image.color=color;return image;}
    static TextMeshProUGUI Text(Transform parent,string name,string value,Vector2 position,Vector2 size,float fontSize)
    {
        var root=Node(parent,name,position,size,new Vector2(0,.5f));var text=root.AddComponent<TextMeshProUGUI>();
        text.font=font;text.fontSharedMaterial=fontMaterial;text.text=value;text.fontSize=fontSize;text.enableAutoSizing=false;
        text.fontSizeMin=22;text.fontSizeMax=fontSize;text.color=new Color(.92f,.95f,.96f);text.alignment=TextAlignmentOptions.Left;
        text.enableWordWrapping=true;text.overflowMode=TextOverflowModes.Ellipsis;text.raycastTarget=false;return text;
    }
    static TextMeshProUGUI AddText(ReferenceCollector rc,Transform parent,string name,string value,float x,float y,float width,float height,float size)
    {
        var text=Text(parent,name,value,new Vector2(x,y),new Vector2(width,height),size);
        text.rectTransform.anchorMin=text.rectTransform.anchorMax=new Vector2(0,1);text.rectTransform.pivot=new Vector2(0,1);text.alignment=TextAlignmentOptions.TopLeft;rc.Add(name,text);return text;
    }
    static Button Button(Transform parent,string name,string label,Vector2 position,Vector2 size)
    {
        var root=Node(parent,name,position,size);var image=Surface(root,new Color(.16f,.28f,.33f));
        var button=root.AddComponent<Button>();button.targetGraphic=image;
        var text=Text(root.transform,"Label",label,new Vector2(-size.x/2+12,0),size-new Vector2(24,8),26);text.alignment=TextAlignmentOptions.Center;
        return button;
    }
}
