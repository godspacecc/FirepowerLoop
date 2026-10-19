using System;
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>制作正式全屏黑幕并调用项目 UI 生成器，不改变现有加载页。</summary>
public static class ArcadeRoundTransitionAuthoring
{
    /// <summary>首次生成后待脚本编译完成，再调用正式生成器的绑定阶段。</summary>
    public static string Bind()
    {
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
            .Invoke(null,new object[]{"Assets/Game/Prefabs/UI/Arcade/ArcadeRoundTransition.prefab","Game.ArcadeRoundTransitionUI"});
        return "已由正式生成器绑定黑幕引用";
    }
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作");
        const string path="Assets/Game/Prefabs/UI/Arcade/ArcadeRoundTransition.prefab";
        var root=new GameObject("ArcadeRoundTransition",typeof(RectTransform),typeof(ReferenceCollector));
        try
        {
            var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var curtain=new GameObject("Curtain",typeof(RectTransform),typeof(Image),typeof(CanvasGroup));curtain.transform.SetParent(root.transform,false);
            rect=(RectTransform)curtain.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            curtain.GetComponent<Image>().color=Color.black;curtain.GetComponent<Image>().raycastTarget=true;
            var group=curtain.GetComponent<CanvasGroup>();group.alpha=0;group.blocksRaycasts=true;group.interactable=false;
            root.GetComponent<ReferenceCollector>().Add("Curtain",group);
            PrefabUtility.SaveAsPrefabAsset(root,path);
            UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            return "黑幕 Prefab 已保存并调用正式 UI 生成器";
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
