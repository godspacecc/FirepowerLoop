using System;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>按已确认三槽预览制作独立Box和宿主列表，使用官方生成工具绑定。</summary>
public static class SurvivorWeaponUiAuthoring
{
    const string Box="Assets/Game/Prefabs/UI/Survivor/Box/SurvivorWeaponSlot.prefab";
    const string Hud="Assets/Game/Prefabs/UI/Survivor/SurvivorHud.prefab";
    static void Guard(){if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("工程或状态不符");}
    public static string MakeBox()
    {
        Guard();var root=new GameObject("SurvivorWeaponSlot",typeof(RectTransform),typeof(Image),typeof(ReferenceCollector));
        try
        {
            ((RectTransform)root.transform).sizeDelta=new Vector2(300,80);root.GetComponent<Image>().color=new Color(.16f,.20f,.25f,1);root.GetComponent<Image>().raycastTarget=false;
            var label=new GameObject("NameText",typeof(RectTransform),typeof(TextMeshProUGUI));label.transform.SetParent(root.transform,false);((RectTransform)label.transform).sizeDelta=new Vector2(290,60);
            var text=label.GetComponent<TextMeshProUGUI>();text.font=AssetDatabase.LoadAssetAtPath<GameObject>(Hud).GetComponent<SurvivorHudUI>().TimeText.font;text.fontSize=28;text.alignment=TextAlignmentOptions.Center;text.text="空武器槽";text.raycastTarget=false;
            root.GetComponent<ReferenceCollector>().Add("NameText",text);PrefabUtility.SaveAsPrefabAsset(root,Box);
        }finally{Object.DestroyImmediate(root);}
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Box));return "武器槽Box已生成，等待编译绑定";
    }
    public static string MakeHud()
    {
        Guard();var root=PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var old=root.transform.Find("WeaponSlots");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var bar=new GameObject("WeaponSlots",typeof(RectTransform),typeof(Image),typeof(HorizontalLayoutGroup),typeof(CustomListView));bar.transform.SetParent(root.transform,false);
            var rect=(RectTransform)bar.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);rect.sizeDelta=new Vector2(990,100);rect.anchoredPosition=new Vector2(0,115);
            bar.GetComponent<Image>().color=new Color(.04f,.07f,.09f,.85f);bar.GetComponent<Image>().raycastTarget=false;
            var layout=bar.GetComponent<HorizontalLayoutGroup>();layout.childAlignment=TextAnchor.MiddleCenter;layout.spacing=20;layout.childControlWidth=layout.childControlHeight=layout.childForceExpandWidth=layout.childForceExpandHeight=false;
            var list=bar.GetComponent<CustomListView>();list.itemPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Box);var rc=root.GetComponent<ReferenceCollector>();rc.Add("WeaponSlots",list);rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,Hud);
        }finally{PrefabUtility.UnloadPrefabContents(root);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Hud));return "HUD三槽已生成，等待编译绑定";
    }
    /// <summary>按用户最终确认把纯经验条贴底；移除旧文本和面板，不改变摇杆祖先布局。</summary>
    public static string MakeBottomExperience()
    {
        Guard();var root=PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var ui=root.GetComponent<SurvivorHudUI>();var rc=root.GetComponent<ReferenceCollector>();
            var oldText=rc.Get<TMP_Text>("ExperienceText");rc.Remove("ExperienceText");if(oldText!=null)Object.DestroyImmediate(oldText.gameObject);
            var panel=root.transform.Find("ExperiencePanel");if(panel!=null)Object.DestroyImmediate(panel.gameObject);
            var host=(RectTransform)root.transform;host.anchorMin=Vector2.zero;host.anchorMax=Vector2.one;host.offsetMin=host.offsetMax=Vector2.zero;
            var bar=(RectTransform)ui.ExperienceBar.transform;bar.anchorMin=Vector2.zero;bar.anchorMax=new Vector2(1,0);bar.sizeDelta=new Vector2(0,12);bar.anchoredPosition=new Vector2(0,6);
            rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,Hud);
        }finally{PrefabUtility.UnloadPrefabContents(root);}
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Hud));return "底部纯经验条已生成";
    }
}
