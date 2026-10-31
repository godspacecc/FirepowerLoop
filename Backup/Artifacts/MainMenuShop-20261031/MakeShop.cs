using System;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
/// <summary>制作已采用的幸存者金币入口，使用正式UI生成器。</summary>
public static class MainMenuShopAuthoring
{
    const string Path="Assets/Game/Prefabs/UI/Home/MainMenuShop.prefab";
    static TMP_FontAsset font;
    public static string MakeEntry()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("工程或状态不符");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab").GetComponent<StartUI>();
        font=source.BtnNewGame.GetComponentInChildren<TMP_Text>(true).font;
        var root=Panel("MainMenuShop",null,1920,1080,0,0,new Color(.02f,.03f,.04f,.8f));
        try {
            var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var rc=root.AddComponent<ReferenceCollector>();
            var panel=Panel("Panel",root.transform,1110,735,0,0,new Color(.137f,.188f,.227f));
            Fixed(Label(panel.transform,"商店",-375,280,210,70,42),"ui.shop.title");
            var balance=Label(panel.transform,"金币  —",235,280,330,50,28);balance.alignment=TextAlignmentOptions.Right;balance.color=new Color(.95f,.8f,.47f);rc.Add("BalanceText",balance);
            var body=Panel("Products",panel.transform,990,360,0,0,new Color(.10f,.15f,.19f));
            var empty=Label(body.transform,"商品暂未开放",0,0,900,80,36);empty.alignment=TextAlignmentOptions.Center;Fixed(empty,"ui.shop.empty");
            rc.Add("ErrorText",Label(panel.transform,"",-100,-320,760,60,23));
            var bg=Panel("BtnBack",panel.transform,280,75,355,-270,new Color(.21f,.27f,.32f));var back=bg.AddComponent<Button>();back.targetGraphic=bg.GetComponent<Image>();
            var label=Label(back.transform,"返回",0,0,260,65,32);label.alignment=TextAlignmentOptions.Center;Fixed(label,"ui.survivor.gold.back");rc.Add("BtnBack",back);
            var close=Panel("BtnClose",panel.transform,65,65,480,285,Color.clear).AddComponent<Button>();close.targetGraphic=close.GetComponent<Image>();var closeText=Label(close.transform,"×",0,0,65,65,36);closeText.alignment=TextAlignmentOptions.Center;rc.Add("BtnClose",close);
            rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,Path);
        }finally{Object.DestroyImmediate(root);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Path));return "金币入口已生成，等待编译回绑";
    }
    static void Rect(RectTransform r,float w,float h,float x,float y){r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x,y);}
    static GameObject Panel(string name,Transform parent,float w,float h,float x,float y,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);Rect((RectTransform)go.transform,w,h,x,y);go.GetComponent<Image>().color=color;return go;}
    static TMP_Text Label(Transform parent,string value,float x,float y,float w,float h,int size){var go=new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);Rect((RectTransform)go.transform,w,h,x,y);var t=go.GetComponent<TextMeshProUGUI>();t.font=font;t.fontSize=size;t.text=value;t.alignment=TextAlignmentOptions.Left;t.raycastTarget=false;return t;}
    static void Fixed(TMP_Text text,string key){var loc=text.GetComponent<LocalizedText>()??text.gameObject.AddComponent<LocalizedText>();loc.Key=key;loc.Texts.Clear();}
}
