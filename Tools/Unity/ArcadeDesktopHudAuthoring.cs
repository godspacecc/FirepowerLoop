using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>制作已确认的 PC HUD；稳定尺寸、两端根节点和全部引用均保存到正式 Prefab。</summary>
public static class ArcadeDesktopHudAuthoring
{
    const string Hud = "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
    const string Box = "Assets/Game/Prefabs/UI/Arcade/Box/ArcadePlayerStatusBox.prefab";
    const string Effect = "Assets/Game/Prefabs/UI/Arcade/Box/ArcadeEffectBox.prefab";
    const string Art = "Assets/Game/Art/UI/UtilityButtons/";

    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅在本工程编辑模式制作 PC HUD");
        CreateSprites();
        BuildBox(); BuildHud(); BuildEffect();
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Box));
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Effect));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Hud));
        return "PC HUD 已制作并执行正式 UI/Box 生成器；编译后需 Bind 回填";
    }

    public static string Bind()
    {
        var boxBind = typeof(BoxUICodeSpawner).GetMethod("BindGeneratedComponent", BindingFlags.Static | BindingFlags.NonPublic);
        boxBind.Invoke(null, new object[] { Box, "Game.ArcadePlayerStatusBoxUI" });
        boxBind.Invoke(null, new object[] { Effect, "Game.ArcadeEffectBoxUI" });
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Hud, "Game.ArcadeHudUI" });
        return "PC HUD 与两个 Box 已回填正式引用";
    }

    /// <summary>保留手机子树；只重制桌面内容并替换对应 ReferenceCollector 项。</summary>
    static void BuildBox()
    {
        var root = PrefabUtility.LoadPrefabContents(Box);
        try
        {
            var view = root.GetComponent<ArcadePlayerStatusBoxUI>();
            var rc = root.GetComponent<ReferenceCollector>();
            var font = view.MobileName;
            var desktop = (RectTransform)view.DesktopContentRoot.transform;
            foreach (var child in desktop.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            desktop.sizeDelta = new Vector2(368, 174);
            Image(desktop, "DesktopShade", 0, 0, 368, 174, Color.white, "PcHudShade.png");
            rc.Remove("TextGold");
            rc.Add("TextName", Text(desktop, font, "TextName", 10, 0, 236, 30, 20));
            rc.Add("TextScore", Text(desktop, font, "TextScore", 10, -24, 240, 58, 40));
            var track = Image(desktop, "HealthTrack", 10, -88, 290, 10, new Color(.04f,.07f,.05f,.9f));
            rc.Add("HealthFill", Fill(track.transform, "HealthFill", false));
            var mult = Text(desktop, font, "TextMultiplier", 258, -29, 66, 46, 32);
            mult.color = new Color(1,.8f,.28f); rc.Add("TextMultiplier", mult);
            var multiplier = Image(desktop, "MultiplierTrack", 332, -6, 10, 92, new Color(.08f,.08f,.06f,.85f));
            rc.Add("MultiplierFill", Fill(multiplier.transform, "MultiplierFill", true));
            rc.Add("TextMultiplierState", Text(desktop,font,"TextMultiplierState",252,-70,74,28,12));
            Image(desktop,"LifeIcon",10,-113,22,28,Color.white,"PcLifePerson.png");
            rc.Add("TextLives",Text(desktop,font,"TextLives",40,-108,42,36,23));
            Image(desktop,"BombIcon",91,-113,26,26,Color.white,"PcBombWhite.png");
            rc.Add("TextBombs",Text(desktop,font,"TextBombs",122,-108,32,36,23));
            rc.Add("TextBombKey",Text(desktop,font,"TextBombKey",156,-113,30,28,14));
            Image(desktop,"DashIcon",211,-113,26,26,Color.white,"DashLightning.png");
            rc.Add("TextDashes",Text(desktop,font,"TextDashes",244,-108,32,36,23));
            rc.Add("TextDashKey",Text(desktop,font,"TextDashKey",279,-113,70,28,14));
            rc.Add("TextState",Text(desktop,font,"TextState",10,-145,348,28,16));
            rc.Sort(); PrefabUtility.SaveAsPrefabAsset(root, Box);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>桌面列表与效果跟随真实玩家行数排版，波次和能力使用独立桌面面板。</summary>
    static void BuildHud()
    {
        var root = PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var ui=root.GetComponent<ArcadeHudUI>(); var rc=root.GetComponent<ReferenceCollector>();
            var existing=root.transform.Find("DesktopInformationRoot");
            var info=existing!=null?(RectTransform)existing:Rect(root.transform,"DesktopInformationRoot",24,-24,368,260);
            var column=info.GetComponent<VerticalLayoutGroup>()??info.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing=4; column.childControlWidth=column.childControlHeight=true;
            column.childForceExpandWidth=column.childForceExpandHeight=false;
            var fitter=info.GetComponent<ContentSizeFitter>()??info.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            ui.PlayerStatusList.transform.SetParent(info,false);
            ((RectTransform)ui.PlayerStatusList.transform).sizeDelta=new Vector2(368,174);
            var rows=ui.PlayerStatusList.GetComponent<VerticalLayoutGroup>();
            rows.spacing=8; rows.childControlWidth=rows.childControlHeight=true;
            rows.childForceExpandWidth=rows.childForceExpandHeight=false;
            var effects=info.Find("DesktopEffectsRoot") as RectTransform ?? Rect(info,"DesktopEffectsRoot",0,0,368,76);
            var horizontal=effects.GetComponent<HorizontalLayoutGroup>()??effects.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.spacing=8; horizontal.childControlWidth=horizontal.childControlHeight=true;
            horizontal.childForceExpandWidth=horizontal.childForceExpandHeight=false;
            MakeEffects(ui.WeaponEffectRoot,ui.WeaponEffectList,effects,68,1);
            MakeEffects(ui.SupportEffectRoot,ui.EffectList,effects,290,4);
            rc.Add("DesktopInformationRoot",info.gameObject);
            rc.Add("DesktopEffectsRoot",effects.gameObject);
            ui.DesktopAbilityRoot.SetActive(false);
            // 联机沿用二次确认退出；桌面提供独立底部小入口，不复用手机右上触控区域。
            if(root.transform.Find("BtnDesktopOnlineExit")==null)
            {
                var exit=Rect(root.transform,"BtnDesktopOnlineExit",-24,24,130,36);
                exit.anchorMin=exit.anchorMax=exit.pivot=new Vector2(1,0);
                exit.anchoredPosition=new Vector2(-24,24);
                var face=exit.gameObject.AddComponent<Image>();face.color=new Color(.02f,.04f,.05f,.6f);
                var buttonExit=exit.gameObject.AddComponent<Button>();buttonExit.targetGraphic=face;
                var caption=Text(exit,ui.TextPauseOnlineExit,"Label",0,0,130,36,16);
                caption.alignment=TextAlignmentOptions.Center;caption.text="退出";
                caption.gameObject.AddComponent<LocalizedText>().Key="ui.arcade.hud.online_exit";
                exit.gameObject.SetActive(false);rc.Add("BtnDesktopOnlineExit",buttonExit);
            }

            if(root.transform.Find("DesktopWaveStatus")==null)
            {
                var wave=Object.Instantiate(ui.TextArena.transform.parent.gameObject,root.transform,false);
                wave.name="DesktopWaveStatus";
                var rect=(RectTransform)wave.transform;
                rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,1);
                rect.anchoredPosition=new Vector2(0,-20); rect.sizeDelta=new Vector2(380,80);
                wave.GetComponent<Image>().color=Color.white;
                var labels=wave.GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach(var waveLabel in labels) { waveLabel.fontSize=waveLabel.fontSizeMax=waveLabel.name=="TextArena"?18:24; waveLabel.fontSizeMin=14; waveLabel.enableAutoSizing=true; waveLabel.rectTransform.sizeDelta=new Vector2(372,waveLabel.name=="TextArena"?30:42); }
                rc.Add("DesktopWaveStatus",wave);
                rc.Add("DesktopTextArena",labels.Single(t=>t.name=="TextArena"));
                rc.Add("DesktopTextWave",labels.Single(t=>t.name=="TextWave"));
            }
            rc.Add("MobileWaveStatus",ui.TextArena.transform.parent.gameObject);
            ui.TextArena.transform.parent.gameObject.SetActive(false);
            var stageShade=root.transform.Find("DesktopWaveStatus").GetComponent<Image>();
            stageShade.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+"PcHudShade.png");stageShade.color=Color.white;
            var desktop=ui.DesktopFieldUpgradeFace;
            foreach(var child in desktop.Cast<Transform>().ToArray())
                if(child.name!="Label" && child!=ui.TextStormCharge.transform) Object.DestroyImmediate(child.gameObject);
            desktop.sizeDelta=new Vector2(180,86);
            var shade=desktop.GetComponent<Image>()??desktop.gameObject.AddComponent<Image>();
            shade.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+"PcHudShade.png"); shade.color=Color.white; shade.raycastTarget=true;
            Image(desktop,"StormIcon",8,-8,42,42,new Color(.1f,.9f,.95f),"StormVortex.png");
            var label=desktop.Find("Label").GetComponent<TextMeshProUGUI>();
            Position(label.rectTransform,54,-33,122,32); label.fontSize=label.fontSizeMax=17; label.fontSizeMin=12; label.enableAutoSizing=true;
            ui.TextStormCharge.transform.SetParent(desktop,false);
            Position(ui.TextStormCharge.rectTransform,62,0,108,36); ui.TextStormCharge.fontSize=ui.TextStormCharge.fontSizeMax=24;
            ui.TextStormCharge.fontSizeMin=16; ui.TextStormCharge.enableAutoSizing=true;
            var chargeTrack=Image(desktop,"StormChargeTrack",8,-70,164,5,new Color(.08f,.17f,.19f));
            var fill=Fill(chargeTrack.transform,"DesktopStormFill",false); fill.color=new Color(.1f,.9f,.95f);
            rc.Add("DesktopStormFill",fill);
            var button=(RectTransform)ui.BtnFieldUpgrade.transform;
            button.anchorMin=button.anchorMax=button.pivot=Vector2.one;button.anchoredPosition=new Vector2(-24,-24);button.sizeDelta=desktop.sizeDelta;
            // 护甲使用两端独立静态节点；手机保持此前正式坐标与字号。
            ui.TextArmor.rectTransform.anchorMin=ui.TextArmor.rectTransform.anchorMax=new Vector2(.5f,1);
            ui.TextArmor.rectTransform.pivot=new Vector2(0,.5f);
            ui.TextArmor.rectTransform.anchoredPosition=new Vector2(170,-205);ui.TextArmor.rectTransform.sizeDelta=new Vector2(380,42);ui.TextArmor.fontSize=26;
            var armor=root.transform.Find("DesktopArmor")?.GetComponent<TextMeshProUGUI>()??Text(root.transform,ui.TextArmor,"DesktopArmor",0,0,200,32,18);
            armor.rectTransform.anchorMin=armor.rectTransform.anchorMax=armor.rectTransform.pivot=Vector2.one;
            armor.rectTransform.anchoredPosition=new Vector2(-24,-122);armor.gameObject.SetActive(false);rc.Add("DesktopArmor",armor);
            rc.Sort(); PrefabUtility.SaveAsPrefabAsset(root,Hud);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void MakeEffects(GameObject group,CustomListView list,Transform parent,float width,int columns)
    {
        group.transform.SetParent(parent,false);
        foreach(var child in group.transform.Cast<Transform>().Where(c=>c!=list.transform).ToArray()) Object.DestroyImmediate(child.gameObject);
        ((RectTransform)group.transform).sizeDelta=new Vector2(width,76);
        var le=group.GetComponent<LayoutElement>()??group.AddComponent<LayoutElement>();le.preferredWidth=width;le.preferredHeight=76;
        Position((RectTransform)list.transform,0,0,width,76);
        var grid=list.GetComponent<GridLayoutGroup>();grid.cellSize=new Vector2(68,76);grid.spacing=new Vector2(6,6);
        grid.constraint=GridLayoutGroup.Constraint.FixedColumnCount;grid.constraintCount=columns;
    }

    /// <summary>桌面新增持续秒数及骷髅环；手机继续使用原图标与末三秒轻闪。</summary>
    static void BuildEffect()
    {
        var root=PrefabUtility.LoadPrefabContents(Effect);
        try
        {
            var view=root.GetComponent<ArcadeEffectBoxUI>();var rc=root.GetComponent<ReferenceCollector>();
            var priorDuration=root.transform.Find("DesktopDuration")?.GetComponent<TextMeshProUGUI>();
            if(priorDuration!=null)
            {
                Position(priorDuration.rectTransform,0,-41,68,20);priorDuration.fontSize=priorDuration.fontSizeMax=14;
                PrefabUtility.SaveAsPrefabAsset(root,Effect);return;
            }
            var duration=Text(root.transform,view.TextFallback,"DesktopDuration",0,-41,68,20,14);
            duration.alignment=TextAlignmentOptions.Center;duration.gameObject.SetActive(false);rc.Add("DesktopDuration",duration);
            var ring=Image(root.transform,"DesktopSkullRing",2,0,64,54,Color.white,"PcEffectRing.png");
            ring.type=UnityEngine.UI.Image.Type.Filled;ring.fillMethod=UnityEngine.UI.Image.FillMethod.Radial360;ring.fillOrigin=2;ring.fillClockwise=false;
            ring.raycastTarget=false;ring.transform.SetAsFirstSibling();ring.gameObject.SetActive(false);rc.Add("DesktopSkullRing",ring);
            rc.Sort();PrefabUtility.SaveAsPrefabAsset(root,Effect);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }

    /// <summary>简单 UI 几何图形离线烘焙为透明 Sprite，不在运行时生成纹理。</summary>
    static void CreateSprites()
    {
        SaveSprite("PcHudShade.png",256,128,(x,y)=>new Color(0.015f,.025f,.03f,.78f*Mathf.SmoothStep(0,1,(1-x)*2)*Mathf.SmoothStep(0,1,y*3)));
        SaveSprite("PcHudSolid.png",8,8,(x,y)=>Color.white);
        SaveSprite("PcLifePerson.png",64,64,(x,y)=>
        {
            bool on=Vector2.Distance(new Vector2(x,y),new Vector2(.5f,.83f))<.105f || (x>.39f&&x<.61f&&y>.37f&&y<.7f)
                || Segment(x,y,.4f,.63f,.28f,.38f)<.052f || Segment(x,y,.6f,.63f,.72f,.38f)<.052f
                || Segment(x,y,.44f,.4f,.4f,.08f)<.055f || Segment(x,y,.56f,.4f,.6f,.08f)<.055f;
            return new Color(1,1,1,on?1:0);
        });
        SaveSprite("PcBombWhite.png",64,64,(x,y)=>new Color(1,1,1,
            Vector2.Distance(new Vector2(x,y),new Vector2(.45f,.42f))<.31f || Segment(x,y,.59f,.67f,.7f,.8f)<.065f || Segment(x,y,.69f,.8f,.85f,.85f)<.035f?1:0));
        SaveSprite("PcEffectRing.png",128,128,(x,y)=>new Color(1,1,1,Mathf.Abs(Vector2.Distance(new Vector2(x,y),new Vector2(.5f,.5f))-.46f)<.025f?1:0));
    }
    static float Segment(float x,float y,float ax,float ay,float bx,float by)
    { var a=new Vector2(ax,ay);var d=new Vector2(bx-ax,by-ay);var p=new Vector2(x,y)-a;return (p-d*Mathf.Clamp01(Vector2.Dot(p,d)/d.sqrMagnitude)).magnitude; }
    static void SaveSprite(string name,int w,int h,Func<float,float,Color> pixel)
    {
        var texture=new Texture2D(w,h,TextureFormat.RGBA32,false);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)texture.SetPixel(x,y,pixel((x+.5f)/w,(y+.5f)/h));
        texture.Apply();File.WriteAllBytes(Art+name,texture.EncodeToPNG());Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(Art+name,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(Art+name);importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;
        importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
    }
    static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
    {var t=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();t.SetParent(parent,false);Position(t,x,y,w,h);return t;}
    static void Position(RectTransform t,float x,float y,float w,float h)
    {t.anchorMin=t.anchorMax=t.pivot=new Vector2(0,1);t.anchoredPosition=new Vector2(x,y);t.sizeDelta=new Vector2(w,h);}
    static Image Image(Transform parent,string name,float x,float y,float w,float h,Color color,string sprite=null)
    {var image=Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;if(sprite!=null){image.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+sprite);if(image.sprite==null)throw new Exception(sprite);}return image;}
    static Image Fill(Transform parent,string name,bool vertical)
    {
        var fill=Image(parent,name,0,0,0,0,Color.white);fill.rectTransform.anchorMin=Vector2.zero;fill.rectTransform.anchorMax=Vector2.one;
        fill.rectTransform.offsetMin=fill.rectTransform.offsetMax=Vector2.zero;fill.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+"PcHudSolid.png");
        fill.type=UnityEngine.UI.Image.Type.Filled;fill.fillMethod=vertical?UnityEngine.UI.Image.FillMethod.Vertical:UnityEngine.UI.Image.FillMethod.Horizontal;fill.fillOrigin=0;return fill;
    }
    static TextMeshProUGUI Text(Transform parent,TextMeshProUGUI template,string name,float x,float y,float w,float h,float size)
    {
        var t=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=template.font;t.fontSharedMaterial=template.fontSharedMaterial;
        t.fontSize=t.fontSizeMax=size;t.fontSizeMin=size-5;t.enableAutoSizing=true;t.enableWordWrapping=false;
        t.fontStyle=FontStyles.Bold;t.color=new Color(.96f,.95f,.89f);t.raycastTarget=false;t.text="";t.overflowMode=TextOverflowModes.Ellipsis;return t;
    }
}
