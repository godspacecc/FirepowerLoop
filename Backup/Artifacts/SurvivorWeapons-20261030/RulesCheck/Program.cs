using System;
using System.Linq;
using System.Reflection;
class Program
{
    static object New(int slots=3) { var t=typeof(Game.SurvivorRunState).Assembly.GetType("Game.SurvivorLoadout"); if(t==null)throw new Exception("missing SurvivorLoadout");return Activator.CreateInstance(t,slots,"Rifle"); }
    static object Call(object s,string name,params object[] args)=>s.GetType().GetMethod(name).Invoke(s,args);
    static void Check(bool condition){if(!condition)throw new Exception("assertion failed");}
    static int Used(object s)=>(int)s.GetType().GetProperty("UsedSlots").GetValue(s);
    static int Main()
    {
        Action[] tests={
            ()=>{var s=New();Check(Used(s)==1&&(int)Call(s,"LevelOf","Rifle")==1&&(int)Call(s,"LevelOf","Drone")==0);},
            ()=>{var s=New();Check((bool)Call(s,"TryUpgrade","Drone",5));Check((bool)Call(s,"TryUpgrade","Drone",5));Check(Used(s)==2&&(int)Call(s,"LevelOf","Drone")==2);},
            ()=>{var s=New(2);Call(s,"TryUpgrade","Drone",5);Check(!(bool)Call(s,"CanUpgrade","Turret",5));Check(!(bool)Call(s,"TryUpgrade","Turret",5));Check((bool)Call(s,"TryUpgrade","Drone",5));},
            ()=>{var s=New();for(int i=0;i<5;i++)Call(s,"TryUpgrade","Drone",5);Check(!(bool)Call(s,"CanUpgrade","Drone",5));Check(!(bool)Call(s,"TryUpgrade","Drone",5));Check((int)Call(s,"LevelOf","Drone")==5);},
            ()=>{var s=New();Check(!(bool)Call(s,"TryUpgrade","",5)&&!(bool)Call(s,"TryUpgrade","Drone",0)&&Used(s)==1);},
            ()=>{var s=New();Call(s,"TryUpgrade","Drone",5);Check((int)Call(New(),"LevelOf","Drone")==0);},
            ()=>{var s=new Game.SurvivorRunState(5,3);s.AddExperience(5);var method=s.GetType().GetMethod("OfferWeighted");if(method==null)throw new Exception("missing OfferWeighted");var args=new object[]{new[]{1,2,3,4},new[]{1,1,1,1},7,4};var a=(int[])method.Invoke(s,args);Check(a.Length==3&&a.Contains(4)&&a.Distinct().Count()==3);args[2]=999;Check(a.SequenceEqual((int[])method.Invoke(s,args)));}
        };
        int failed=0;for(int i=0;i<tests.Length;i++){try{tests[i]();Console.WriteLine($"PASS {i+1}");}catch(Exception e){failed++;Console.WriteLine($"FAIL {i+1}: {e.Message}");}}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed; standalone pure C# checks, not Unity tests");return failed==0?0:1;
    }
}
