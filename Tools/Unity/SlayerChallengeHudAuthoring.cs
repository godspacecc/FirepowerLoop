using System;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>在原 HUD 序列化一个Slayer选项按钮；复用现有字体、样式和正式 UI 生成器。</summary>
public static class SlayerChallengeHudAuthoring
{
    const string Path="Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
    static void Guard(){if(Application.dataPath!="E:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying)throw new InvalidOperationException("仅本项目编辑模式可制作Slayer HUD");}
    public static string Build()
    {
        Guard();var root=PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var old=root.transform.Find("BtnSlayerChallenge");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var source=root.GetComponent<ArcadeHudUI>().BtnFieldUpgrade;
            if(source==null)throw new InvalidOperationException("原 HUD 风暴按钮丢失");
            var button=Object.Instantiate(source,root.transform);button.name="BtnSlayerChallenge";
            button.onClick=new Button.ButtonClickedEvent();
            var rect=(RectTransform)button.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);
            rect.anchoredPosition=new Vector2(360,-380);rect.sizeDelta=new Vector2(280,96);
            var text=button.GetComponentInChildren<TextMeshProUGUI>(true);text.text="Slayer选项";
            var localized=text.GetComponent<LocalizedText>();if(localized!=null)Object.DestroyImmediate(localized);
            button.gameObject.SetActive(false);
            var references=root.GetComponent<ReferenceCollector>();references.data.RemoveAll(e=>e.key=="BtnSlayerChallenge");references.Add("BtnSlayerChallenge",button);references.Sort();
            PrefabUtility.SaveAsPrefabAsset(root,Path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        if(!EditorApplication.ExecuteMenuItem("Assets/智能更新UI"))throw new InvalidOperationException("HUD 正式生成入口未执行");
        return "Slayer选项按钮已序列化，等待正式生成组件编译后绑定";
    }
    public static string Bind()
    {
        Guard();typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",BindingFlags.Static|BindingFlags.NonPublic)
            .Invoke(null,new object[]{Path,"Game.ArcadeHudUI"});return "Slayer选项按钮引用已由正式生成器绑定";
    }
}
