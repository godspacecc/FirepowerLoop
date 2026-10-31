using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game;
using QFramework;
using UnityEngine;
public class Script
{
 public static object Main(){Run().Forget();return "启动真实YooAsset与Start/Restart验证，使用临时双账本";}
 static async UniTaskVoid Run()
 {
  const string output="E:/GitHub/FirepowerLoop/Backup/Artifacts/CharacterPurchase-20261031/ui/runtime.txt";
  var log=new List<string>();var arch=Game.Game.Interface;var system=arch.GetSystem<SurvivorSystem>();
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;var gold=typeof(SurvivorSystem).GetField("goldStore",flags);var result=typeof(SurvivorSystem).GetField("store",flags);
  var oldGold=gold.GetValue(system);var oldResult=result.GetValue(system);var root=Path.Combine(Path.GetTempPath(),"CharacterUiRuntime-"+Guid.NewGuid().ToString("N"));
  try {
   Directory.CreateDirectory(Path.Combine(root,"Saves"));File.WriteAllText(Path.Combine(root,"Saves/survivor-gold-1.json"),"{\"Version\":1,\"Balance\":100,\"ActiveRun\":\"test\",\"Sequence\":0}");
   gold.SetValue(system,new SurvivorGoldFileStore(1,root));result.SetValue(system,new SurvivorResultFileStore(1,root));
   log.Add("loader="+ResourceLoader.Current.GetType().FullName);
   foreach(var path in new[]{"Prefabs/UI/Home/MainMenuShop","Prefabs/UI/Survivor/SurvivorEntry","Prefabs/UI/Survivor/Box/CharacterCardBox","tbsurvivorcharacter.bytes"}) {
    bool done=false;UnityEngine.Object asset=null;var handle=ResourceLoader.LoadAssetAsync<UnityEngine.Object>(path,a=>{asset=a;done=true;});
    float deadline=Time.realtimeSinceStartup+20;while(!done&&Time.realtimeSinceStartup<deadline)await UniTask.NextFrame();log.Add(path+" loaded="+(asset!=null));ResourceLoader.Release(handle);
   }
   if(!system.TryPurchaseCharacter("survivor.character.1",out var error)||!system.TrySelectCharacter(1,out error))throw new Exception(error);
   var flow=arch.GetSystem<SurvivorSceneFlowSystem>();var started=await flow.StartAsync();log.Add("StartAsync="+started.Success+" "+started.Error);if(!started.Success)throw new Exception(started.Error);
   LogAppearance(log,"start");
   var restarted=await flow.RestartAsync();log.Add("RestartAsync="+restarted.Success+" "+restarted.Error);if(!restarted.Success)throw new Exception(restarted.Error);
   LogAppearance(log,"restart");var returned=await flow.ReturnToMenuAsync();log.Add("Return="+returned.Success+" "+returned.Error);
  }catch(Exception e){log.Add(e.ToString());}
  finally {system.Stop();gold.SetValue(system,oldGold);result.SetValue(system,oldResult);log.Add("original stores restored; temporary ledger="+root);File.WriteAllLines(output,log);}
 }
 static void LogAppearance(List<string> log,string stage){var view=UnityEngine.Object.FindObjectOfType<ClothingAppearanceView>();if(view==null)throw new Exception("missing real player appearance");var id=typeof(ClothingAppearanceView).GetField("wholeBodyIndex",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);log.Add(stage+" real PlayerCtrl appearance="+id+" scale="+view.transform.localScale);if((int)id!=1)throw new Exception("selected appearance was not applied");}
}
