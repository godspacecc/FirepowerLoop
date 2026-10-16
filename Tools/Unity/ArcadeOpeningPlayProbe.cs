using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>临时编辑器试玩探针：使用正式入口及虚拟手柄，只写临时档；不改战斗规则或清怪跳轮。</summary>
public static class ArcadeOpeningPlayProbe
{
    const string Output="Assets/Tests/Results~/ArcadeOpening-20261011";
    static Gamepad pad;
    static Mouse mouse;
    static bool mouseWasEnabled;
    static int lastWave;
    static bool firstPicture, fourthPicture, exitPicture;
    static double exitAt;
    public static string Start()
    {
        var root=Path.Combine(Path.GetTempPath(),"FirepowerLoopOpeningPlay-"+Guid.NewGuid().ToString("N"));
        Game.Game.Interface.GetSystem<Game.SaveSystem>().ConfigureForTests(root);
        Game.Game.Interface.GetSystem<Game.ArcadeRunSystem>().ConfigureForTests(root);
        UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
            .Single(b=>b.name=="BtnNewGame").onClick.Invoke();
        Directory.CreateDirectory(Output);
        return root;
    }
    public static string Open()
    {
        if(!Game.ArcadeResumeUICtrl.TryOpenEntry(out var error))return error;
        return "Entry opened";
    }
    public static string Enter()
    {
        UnityEngine.Object.FindObjectOfType<Game.ArcadeResumeUI>().BtnContinue.onClick.Invoke();
        return "Official start invoked";
    }
    public static string Drive()
    {
        Stop();
        pad=InputSystem.AddDevice<Gamepad>("ArcadeOpeningProbePad");
        mouse=Mouse.current; mouseWasEnabled=mouse!=null&&mouse.enabled;
        if(mouseWasEnabled)InputSystem.DisableDevice(mouse);
        EditorApplication.update+=Tick;
        return "Driving through the real controller input; no health or enemy mutations";
    }
    static object Field(object obj,string name)=>obj.GetType().GetField(name).GetValue(obj);
    static object Runtime()=>typeof(Game.ArcadeCombatModel).GetProperty("Runtime",BindingFlags.Instance|BindingFlags.NonPublic)
        .GetValue(Game.Game.Interface.GetModel<Game.ArcadeCombatModel>());
    public static string Status()
    {
        var a=Game.Game.Interface;
        if(a.GetSystem<Game.ArcadeCombatSystem>().TryGetTestStatus(out var text,out var error))return text;
        return a.GetModel<Game.ArcadeRunModel>().Phase+" "+error;
    }
    static void Tick()
    {
        if(!EditorApplication.isPlaying||pad==null){Stop();return;}
        var r=Runtime(); if(r==null){Stop();return;}
        var p=(Vector2)Field(r,"PlayerPosition");
        int wave=(int)Field(r,"WaveIndex")+1;
        int pending=((ICollection)Field(r,"SpawnQueue")).Count;
        var enemies=(IDictionary)Field(r,"Enemies");
        var total=enemies.Count+pending;
        if(lastWave!=wave){lastWave=wave;File.AppendAllText(Output+"/play.log",DateTime.Now+" "+Status()+"\n");}
        if(wave==1&&!firstPicture&&total<100&&total>70){firstPicture=true;ScreenCapture.CaptureScreenshot(Output+"/round1.png");}
        if(wave==4&&!fourthPicture&&total<130){fourthPicture=true;ScreenCapture.CaptureScreenshot(Output+"/round4.png");}
        Vector2 move,aim=Vector2.up;
        bool exit=(bool)Field(r,"AwaitingExit");
        if(exit)
        {
            if(!exitPicture){exitPicture=true;exitAt=EditorApplication.timeSinceStartup;ScreenCapture.CaptureScreenshot(Output+"/exits.png");}
            move=EditorApplication.timeSinceStartup-exitAt<2?Vector2.zero:(new Vector2(-2.5f,16.5f)-p).normalized;
            aim=Vector2.zero;
        }
        else
        {
            float angle=Mathf.Atan2(p.y,p.x)+.35f;
            var target=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*13.8f;
            move=(target-p).normalized;
            float closest=float.MaxValue;
            foreach(var enemy in enemies.Values)
            {
                var delta=(Vector2)Field(enemy,"Position")-p;
                if(delta.sqrMagnitude<closest){closest=delta.sqrMagnitude;aim=delta.normalized;}
            }
            if((bool)Field(r,"WaveClearAwarded") && EditorApplication.timeSinceStartup%1<.4)aim=Vector2.zero;
        }
        InputSystem.QueueStateEvent(pad,new GamepadState{leftStick=move,rightStick=aim});
    }
    public static string Stop()
    {
        EditorApplication.update-=Tick;
        if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);pad=null;
        if(mouseWasEnabled&&mouse!=null&&mouse.added)InputSystem.EnableDevice(mouse);
        mouse=null;mouseWasEnabled=false;
        return "Probe input released";
    }
}
