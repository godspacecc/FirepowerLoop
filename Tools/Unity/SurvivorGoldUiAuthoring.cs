using System;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
/// <summary>制作已采用的幸存者金币入口，使用正式UI生成器。</summary>
public static class SurvivorGoldUiAuthoring
{
    const string Path="Assets/Game/Prefabs/UI/Survivor/SurvivorEntry.prefab";
    static TMP_FontAsset font;
    public static string MakeEntry()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("工程或状态不符");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab").GetComponent<StartUI>();
        font=source.BtnNewGame.GetComponentInChildren<TMP_Text>(true).font;
        var root=Panel("SurvivorEntry",null,1920,1080,0,0,new Color(.02f,.03f,.04f,.8f));
        try {
            var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var rc=root.AddComponent<ReferenceCollector>();
            var panel=Panel("Panel",root.transform,1110,735,0,0,new Color(.137f,.188f,.227f));
            Fixed(Label(panel.transform,"幸存者模式",0,280,990,70,48),"ui.survivor.mode");
            Fixed(Label(panel.transform,"持有金币",0,165,990,50,32),"ui.survivor.gold.owned");
            var balance=Label(panel.transform,"0",0,70,990,100,72);balance.color=new Color(.95f,.8f,.47f);rc.Add("BalanceText",balance);
            Fixed(Label(panel.transform,"击败敌人有概率掉落金币，拾取后永久保留。",0,-55,990,55,28),"ui.survivor.gold.help");
            Fixed(Label(panel.transform,"金币将用于后续开放的局外成长。",0,-115,990,55,28),"ui.survivor.gold.future");
            rc.Add("ErrorText",Label(panel.transform,"",0,-335,990,50,23));
            for(int i=0;i<2;i++){
                var bg=Panel(i==0?"BtnBack":"BtnStart",panel.transform,280,85,0,0,i==0?new Color(.21f,.27f,.32f):new Color(.77f,.61f,.31f));var b=bg.AddComponent<Button>();b.targetGraphic=bg.GetComponent<Image>();
                Rect((RectTransform)b.transform,i==0?280:600,85,i==0?-345:160,-245);
                var label=Label(b.transform,i==0?"返回":"开始生存",0,0,i==0?260:580,75,32);label.alignment=TextAlignmentOptions.Center;label.color=i==0?Color.white:new Color(.09f,.12f,.15f);Fixed(label,i==0?"ui.survivor.gold.back":"ui.survivor.gold.start");rc.Add(b.name,b);
            }
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
