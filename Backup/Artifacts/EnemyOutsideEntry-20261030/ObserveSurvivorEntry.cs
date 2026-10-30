using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Game;
using QFramework;
using UnityEngine;
using UnityEditor;

/// <summary>真实菜单入口的只读场外状态观察；不保存本局，不改正式资源。</summary>
public static class ObserveSurvivorEntry
{
    const string Folder="E:/GitHub/FirepowerLoop/Backup/Artifacts/EnemyOutsideEntry-20261030/";
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static double deadline,next; static int capture; static float first=-1;
    static string tag="live-survivor-entry";
    public static string RunEdge()
    {
        tag="live-survivor-edge";
        var system=Game.Game.Interface.GetSystem<SurvivorSystem>();
        typeof(SurvivorSystem).GetField("player",Hidden).SetValue(system,new Vector2(27,-14));
        File.WriteAllText(Folder+tag+".csv","elapsed,id,x,z,outside,health,actorActive,renderersEnabled\n");
        deadline=EditorApplication.timeSinceStartup+10;next=0;first=-1;capture=0;
        EditorApplication.update-=Observe;EditorApplication.update+=Observe;EditorApplication.isPaused=false;
        return "Controlled test: player placed near right boundary; normal movement/shooting/damage unchanged";
    }
    public static string Run()
    {
        var ui=UnityEngine.Object.FindObjectOfType<StartUI>();
        if(!Application.isPlaying||ui==null||!ui.BtnSurvivor.interactable)throw new InvalidOperationException("真实幸存者按钮尚未可用");
        File.WriteAllText(Folder+"live-survivor-entry.csv","elapsed,id,x,z,outside,health,actorActive,renderersEnabled\n");
        deadline=EditorApplication.timeSinceStartup+50; next=0;first=-1;capture=0;
        EditorApplication.update-=Observe;EditorApplication.update+=Observe;
        ui.BtnSurvivor.onClick.Invoke();return "Clicked real Survivor button; observing up to 50 seconds";
    }
    static void Observe()
    {
        if(!Application.isPlaying||EditorApplication.timeSinceStartup>deadline){EditorApplication.update-=Observe;return;}
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.05;
        var system=Game.Game.Interface.GetSystem<SurvivorSystem>();
        var enemies=(IDictionary)typeof(SurvivorSystem).GetField("enemies",Hidden).GetValue(system);
        if(enemies.Count==0)return;
        var model=Game.Game.Interface.GetModel<SurvivorModel>();
        float elapsed=Time.time;
        if(first<0)first=elapsed;
        var presentation=Game.Game.Interface.GetUtility<IArcadeEnemyPresentationPort>();
        var actors=(IDictionary)presentation.GetType().GetField("arcadeEnemies",Hidden).GetValue(presentation);
        foreach(var e in enemies.Values)
        {
            int id=(int)e.GetType().GetField("Id").GetValue(e);
            var position=(Vector2)e.GetType().GetField("Position").GetValue(e);
            var outside=(FirepowerLoop.Arcade.ArcadeOutsideEntryState)e.GetType().GetField("OutsideEntry").GetValue(e);
            int health=(int)e.GetType().GetField("Health").GetValue(e);
            var item=actors[id];var view=item==null?null:(EnemyView)item.GetType().GetProperty("View").GetValue(item);
            int renderers=view==null?0:view.GetComponentsInChildren<Renderer>().Count(r=>r.enabled&&r.gameObject.activeInHierarchy);
            File.AppendAllText(Folder+tag+".csv",FormattableString.Invariant($"{elapsed:F4},{id},{position.x:F4},{position.y:F4},{outside.Active},{health},{view!=null&&view.gameObject.activeInHierarchy},{renderers}\n"));
        }
        if(capture<3&&elapsed-first>=capture*(tag.EndsWith("edge")?.2f:.8f)){ScreenCapture.CaptureScreenshot(Folder+tag+"-"+capture+".png");capture++;}
        if(elapsed-first>2.5f){EditorApplication.update-=Observe;EditorApplication.isPaused=true;}
    }
}
