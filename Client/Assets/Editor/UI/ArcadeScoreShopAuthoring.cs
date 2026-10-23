using System;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>按已采用ScoreShop-v2制作正式三卡Prefab；稳定布局和引用由Prefab及生成器拥有。</summary>
public static class ArcadeScoreShopAuthoring
{
    private const string Root="Assets/Game/Prefabs/UI/Arcade/";
    private static readonly Color Ivory=new Color(.94f,.95f,.96f),Muted=new Color(.65f,.71f,.76f),Gold=new Color(.96f,.77f,.42f);
    private static TMP_FontAsset font;
    /// <summary>只制作本任务两个独立Prefab，不读写存档，也不改变Boot场景。</summary>
    public static string Apply()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("只允许本项目编辑态制作");
        font=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"ArcadeResume.prefab").GetComponentInChildren<TMP_Text>(true).font;
        var card=Node(null,"ArcadeScoreShopOfferBox",0,0,320,330);var refs=card.gameObject.AddComponent<ReferenceCollector>();
        try
        {
            Face(card,new Color(.10f,.15f,.19f));
            var outline=card.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.21f,.27f,.32f);outline.effectDistance=new Vector2(1,-1);
            var accent=Node(card,"Accent",0,0,320,3);Face(accent,Gold);
            Collect(refs,Text(card,"TextType","空投武器 · 25秒",24,20,270,25,16,Gold));
            var icon=Node(card,"Icon",24,65,58,58).gameObject.AddComponent<ArcadeScoreShopIcon>();icon.color=Gold;icon.raycastTarget=false;refs.Add("Icon",icon);
            Collect(refs,Text(card,"TextName","速射武器",24,138,272,40,28,Ivory));
            var desc=Text(card,"TextDescription","下一关领取现有武器。\n效果与同枪强化均沿用空投规则。",24,188,272,58,18,Muted);desc.enableWordWrapping=true;Collect(refs,desc);
            var buy=Button(card,"BtnBuy",24,266,272,44,Gold);refs.Add("BtnBuy",buy);
            Collect(refs,Text(buy.transform,"TextBuy","购买",14,0,75,44,19,new Color(.07f,.10f,.14f)));
            var price=Text(buy.transform,"TextPrice","3,000 积分",90,0,167,44,19,new Color(.07f,.10f,.14f));price.alignment=TextAlignmentOptions.MidlineRight;Collect(refs,price);
            refs.Sort();PrefabUtility.SaveAsPrefabAsset(card.gameObject,Root+"Box/ArcadeScoreShopOfferBox.prefab");
        }
        finally{Object.DestroyImmediate(card.gameObject);}
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Box/ArcadeScoreShopOfferBox.prefab"));
        var root=Node(null,"ArcadeScoreShop",0,0,1920,1080);root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.sizeDelta=Vector2.zero;
        refs=root.gameObject.AddComponent<ReferenceCollector>();Face(root,new Color(.045f,.065f,.09f,.97f));
        try
        {
            var board=Node(root,"Board",0,0,1000,700);board.anchorMin=board.anchorMax=board.pivot=new Vector2(.5f,.5f);board.anchoredPosition=Vector2.zero;
            Fixed(board,"Brand","brand","FIREPOWERLOOP / FIELD SHOP",0,10,1000,30,16,Muted);
            Fixed(board,"Title","title","下一关，怎么打？",0,63,710,62,44,Ivory);
            Fixed(board,"Subtitle","subtitle","三件商品，最多购买一件。\n也可以保留积分，直接出发。",0,134,700,58,20,Muted).enableWordWrapping=true;
            var label=Fixed(board,"ScoreLabel","score_label","当前积分",735,75,265,28,17,Muted);label.alignment=TextAlignmentOptions.MidlineRight;
            var score=Text(board,"TextScore","12,500",735,105,265,62,46,Gold);score.alignment=TextAlignmentOptions.MidlineRight;Collect(refs,score);
            var listRoot=Node(board,"OfferList",0,218,1000,330);var grid=listRoot.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize=new Vector2(320,330);grid.spacing=new Vector2(20,0);grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=3;
            var list=listRoot.gameObject.AddComponent<CustomListView>();list.itemPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Box/ArcadeScoreShopOfferBox.prefab");refs.Add("OfferList",list);
            var refresh=Button(board,"BtnRefresh",235,574,300,48,new Color(.19f,.17f,.13f));refs.Add("BtnRefresh",refresh);
            var refreshText=Text(refresh.transform,"TextRefresh","刷新商品 · 500 积分",10,0,280,48,20,Gold);refreshText.alignment=TextAlignmentOptions.Center;Collect(refs,refreshText);
            var skip=Button(board,"BtnSkip",552,574,248,48,new Color(.10f,.14f,.18f));refs.Add("BtnSkip",skip);
            var skipLabel=Fixed(skip.transform,"Label","skip","跳过，保留积分 →",8,0,232,48,19,Ivory);skipLabel.alignment=TextAlignmentOptions.Center;
            var rule=Fixed(board,"Rule","rule","仅出售现有空投物品，下一关开始时领取，沿用原有持续时间。\n购买与刷新都会扣除积分，推迟下一次加命。刷新每次固定500积分。",0,640,1000,52,16,Muted);
            rule.enableWordWrapping=true;rule.alignment=TextAlignmentOptions.Center;
            var message=Text(board,"TextMessage","",0,695,1000,40,18,Gold);message.alignment=TextAlignmentOptions.Center;Collect(refs,message);
            refs.Sort();PrefabUtility.SaveAsPrefabAsset(root.gameObject,Root+"ArcadeScoreShop.prefab");
        }
        finally{Object.DestroyImmediate(root.gameObject);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"ArcadeScoreShop.prefab"));
        return "采用版三卡Prefab和外部Box已制作，正式生成器已运行";
    }
    /// <summary>新生成代码完成编译后再次调用生成器的正式回绑。</summary>
    public static string Bind()
    {
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Root+"ArcadeScoreShop.prefab","Game.ArcadeScoreShopUI"});
        typeof(BoxUICodeSpawner).GetMethod("BindGeneratedComponent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Root+"Box/ArcadeScoreShopOfferBox.prefab","Game.ArcadeScoreShopOfferBoxUI"});
        return "积分商店View已回绑";
    }
    private static void Collect(ReferenceCollector collector,TMP_Text text)=>collector.Add(text.name,text);
    private static RectTransform Node(Transform parent,string name,float x,float y,float width,float height)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(width,height);return rect;
    }
    private static Image Face(RectTransform rect,Color color)
    {var image=rect.gameObject.AddComponent<Image>();image.color=color;return image;}
    private static TMP_Text Text(Transform parent,string name,string preview,float x,float y,float w,float h,float size,Color color)
    {
        var text=Node(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSharedMaterial=font.material;
        text.text=preview;text.color=color;text.fontSize=text.fontSizeMax=size;text.fontSizeMin=size*.75f;text.enableAutoSizing=true;
        text.raycastTarget=false;text.enableWordWrapping=false;text.alignment=TextAlignmentOptions.MidlineLeft;return text;
    }
    private static TMP_Text Fixed(Transform parent,string name,string suffix,string preview,float x,float y,float w,float h,float size,Color color)
    {
        var text=Text(parent,name,preview,x,y,w,h,size,color);var loc=text.gameObject.AddComponent<LocalizedText>();loc.Key="ui.arcade.score_shop."+suffix;loc.Texts.Clear();text.text=preview;return text;
    }
    private static Button Button(Transform parent,string name,float x,float y,float w,float h,Color color)
    {
        var rect=Node(parent,name,x,y,w,h);var face=Face(rect,color);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=face;
        var colors=button.colors;colors.highlightedColor=new Color(1.15f,1.15f,1.15f);colors.disabledColor=new Color(.4f,.4f,.4f,.5f);button.colors=colors;return button;
    }
}
