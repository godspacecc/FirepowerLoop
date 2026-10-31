using System;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
/// <summary>制作已确认角色商店与选角正式资源，布局仅保存在Prefab。</summary>
public static class CharacterUiAuthoring
{
    static TMP_FontAsset font;
    const string BoxPath="Assets/Game/Prefabs/UI/Survivor/Box/CharacterCardBox.prefab";
    public static string Apply()
    {
        if(EditorApplication.isPlaying || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets") throw new Exception("工程状态不符");
        font=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab").GetComponent<StartUI>().BtnNewGame.GetComponentInChildren<TMP_Text>(true).font;
        System.IO.Directory.CreateDirectory("Assets/Game/Prefabs/UI/Survivor/Box");
        var card=Panel("CharacterCardBox",null,210,440,0,0,new Color(.10f,.15f,.19f));
        var refs=card.AddComponent<ReferenceCollector>();
        var edge=card.AddComponent<Outline>();edge.effectDistance=new Vector2(2,-2);refs.Add("SelectionOutline",edge);
        var portrait=Panel("Portrait",card.transform,190,285,0,69,Color.white).GetComponent<Image>();portrait.preserveAspect=true;refs.Add("Portrait",portrait);
        refs.Add("NameText",Label(card.transform,"默认角色",0,-110,184,55,23));
        var b=Panel("BtnAction",card.transform,190,55,0,-175,new Color(.22f,.30f,.35f)).AddComponent<Button>();b.targetGraphic=b.GetComponent<Image>();refs.Add("BtnAction",b);
        refs.Add("ActionText",Label(b.transform,"选择",0,0,170,50,22));
        refs.Sort();PrefabUtility.SaveAsPrefabAsset(card,BoxPath);Object.DestroyImmediate(card);
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath));
        Make(false);Make(true);return "正式Box及两页已生成";
    }
    static void Make(bool selection)
    {
        string name=selection?"SurvivorEntry":"MainMenuShop";
        string path="Assets/Game/Prefabs/UI/"+(selection?"Survivor/":"Home/")+name+".prefab";
        var root=Panel(name,null,1920,1080,0,0,new Color(.02f,.03f,.04f,.8f));
        try {
            var r=(RectTransform)root.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            var rc=root.AddComponent<ReferenceCollector>();
            var board=Panel("Panel",root.transform,1450,680,0,0,new Color(.137f,.188f,.227f));
            Fixed(Label(board.transform,selection?"选择角色":"商店",-460,280,450,65,36),selection?"ui.character.select_title":"ui.shop.title");
            rc.Add("BalanceText",Label(board.transform,"金币  —",490,280,300,50,26));
            var listRoot=Panel("Cards",board.transform,1370,440,0,18,Color.clear);
            var grid=listRoot.AddComponent<GridLayoutGroup>();grid.cellSize=new Vector2(210,440);grid.spacing=new Vector2(22,0);grid.childAlignment=TextAnchor.MiddleCenter;grid.constraint=GridLayoutGroup.Constraint.FixedRowCount;grid.constraintCount=1;
            var list=listRoot.AddComponent<CustomListView>();list.itemPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath);rc.Add("Cards",list);
            foreach(int id in new[]{0,1,7,3,4,9})rc.Add("Portrait"+id,AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Art/CharacterPortraits/character-"+id+".png"));
            Fixed(Label(board.transform,selection?"未解锁角色可在商店购买。":"购买后永久解锁，角色只有外观差异。",-230,-280,900,48,22),selection?"ui.character.select_help":"ui.character.shop_help");
            rc.Add("ErrorText",Label(board.transform,"",0,-220,1320,42,22));
            var back=Panel("BtnBack",board.transform,170,60,selection?330:580,-280,new Color(.22f,.30f,.35f)).AddComponent<Button>();back.targetGraphic=back.GetComponent<Image>();rc.Add("BtnBack",back);Fixed(Label(back.transform,"返回",0,0,150,55,26),"ui.survivor.gold.back");
            if(selection){var start=Panel("BtnStart",board.transform,245,60,560,-280,new Color(.70f,.55f,.27f)).AddComponent<Button>();start.targetGraphic=start.GetComponent<Image>();rc.Add("BtnStart",start);Fixed(Label(start.transform,"开始生存",0,0,230,55,26),"ui.survivor.gold.start");}
            var close=Panel("BtnClose",board.transform,50,50,680,280,Color.clear).AddComponent<Button>();close.targetGraphic=close.GetComponent<Image>();Label(close.transform,"×",0,0,45,45,32);rc.Add("BtnClose",close);
            rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,path);
        }finally {Object.DestroyImmediate(root);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));
    }
    static void Rect(RectTransform r,float w,float h,float x,float y){r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x,y);}
    static GameObject Panel(string name,Transform parent,float w,float h,float x,float y,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);Rect((RectTransform)go.transform,w,h,x,y);go.GetComponent<Image>().color=color;return go;}
    static TMP_Text Label(Transform parent,string value,float x,float y,float w,float h,int size){var go=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);Rect((RectTransform)go.transform,w,h,x,y);var t=go.GetComponent<TextMeshProUGUI>();t.font=font;t.fontSize=size;t.text=value;t.alignment=TextAlignmentOptions.Left;t.raycastTarget=false;return t;}
    static void Fixed(TMP_Text text,string key){var loc=text.GetComponent<LocalizedText>()??text.gameObject.AddComponent<LocalizedText>();loc.Key=key;loc.Texts.Clear();}
}
