from pathlib import Path
p=Path('Backup/Artifacts/SpecialZombieImplementation-20261101/stage2/RuntimeSample.cs');s=p.read_text(encoding='utf-8-sig').replace('public class Script {','public class SpecialFireballSample {').replace('for(int kind=0;kind<3;kind++)','for(int kind=1;kind<2;kind++)').replace('Dir+"/runtime.txt"','Dir+"/runtime-fireball.txt"').replace('Dir+"/start.txt"','Dir+"/start-fireball.txt"')
a=s.index(' foreach(var name in new[]{"Exploder","Caster","Shield"})');b=s.index(' // Hide existing actors',a);s=s[:a]+s[b:]
s=s.replace('for(int i=0;i<40;i++){','var particleAges=new Dictionary<int,float>();\n for(int i=0;i<40;i++){')
s=s.replace('foreach(GameObject o in current.Values)Layer(o);','''foreach(GameObject o in current.Values){Layer(o);int key=o.GetInstanceID();float age=particleAges.TryGetValue(key,out var lastAge)?lastAge+.1f:.1f;particleAges[key]=age;foreach(var ps in o.GetComponentsInChildren<ParticleSystem>(true)){ps.Simulate(age,false,true,true);ps.Pause();}}''')
s=s.replace('using Game;using QFramework;','using Game;using QFramework;using Cysharp.Threading.Tasks;')
s+='''
public class Script {
public static object Main(){Run().Forget();return "Real Boot Start + corrected formal fireball sampling dispatched";}
static async UniTaskVoid Run(){string dir="E:/GitHub/FirepowerLoop/Backup/Artifacts/SpecialZombieImplementation-20261101/stage2";try{var a=Game.Game.Interface;var s=a.GetSystem<SurvivorSystem>();var f=BindingFlags.NonPublic|BindingFlags.Instance;typeof(SurvivorSystem).GetField("goldStore",f).SetValue(s,new SurvivorGoldFileStore(1,dir+"/runtime-save"));typeof(SurvivorSystem).GetField("store",f).SetValue(s,new SurvivorResultFileStore(1,dir+"/runtime-save"));var r=await a.GetSystem<SurvivorSceneFlowSystem>().StartAsync();File.WriteAllText(dir+"/start-fireball.txt",r.Success+" | "+r.Error);if(!r.Success)throw new Exception(r.Error);SpecialFireballSample.Main();File.AppendAllText(dir+"/runtime-fireball.txt","\\nFireball ParticleSystems explicitly Simulate age at 0.1 seconds per sampled frame. No production effect changes.\\n");}catch(Exception e){File.WriteAllText(dir+"/runtime-fireball-error.txt",e.ToString());}}
}
'''
Path('Backup/Artifacts/SpecialZombieImplementation-20261101/stage2/RuntimeSampleFireball.cs').write_text(s,encoding='utf-8')
