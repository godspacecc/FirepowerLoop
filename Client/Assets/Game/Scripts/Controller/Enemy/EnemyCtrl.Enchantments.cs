using System.Collections.Generic;
using UnityEngine;
namespace Game
{
 public sealed partial class EnemyCtrl
 {
  /// <summary>附魔波纹独立有界池；饱和只复用旧视觉，不改变授予规则。</summary>
  private const int EnchantPulseCapacity=24;
  private const float EnchantPulseLifetime=1.3f;
  private readonly List<EnchantPulseEntry> enchantPulses=new();
  private int enchantPulseCursor;
  /// <summary>纯表现年龄、半径与渲染器，生命周期由原控制器推进。</summary>
  private sealed class EnchantPulseEntry{public LineRenderer Ring;public float Age,Radius;}
  public void SetSurvivorEnchantment(int id,SurvivorEnchantSnapshot snapshot)
  {if(arcadeEnemies.TryGetValue(id,out var entry))entry.View?.SetSurvivorEnchantment(snapshot);}
  /// <summary>仅接收当前地图的通知，模板来自已提交的敌人依赖，无异步加载。</summary>
  public void PlaySurvivorEnchantPulse(int id,string kind,Vector3 position,float radius)
  {
   if(released||arcadeRoot==null||!arcadeRoot.gameObject.activeInHierarchy||radius<=0||float.IsNaN(radius)||float.IsInfinity(radius))return;
   int index=kind=="EnchantHealth"?0:kind=="EnchantSpeed"?1:kind=="EnchantShield"?2:-1;if(index<0)return;
   LineRenderer template=null;
   foreach(var cache in committedPrefabs.Values){if(cache.Prefab!=null){template=cache.Prefab.GetComponent<EnemyView>()?.SurvivorEnchantRingTemplate;if(template!=null)break;}}
   if(template==null)return;
   EnchantPulseEntry entry;
   if(enchantPulses.Count<EnchantPulseCapacity){entry=new EnchantPulseEntry{Ring=UnityEngine.Object.Instantiate(template,arcadeRoot,false)};enchantPulses.Add(entry);entry.Ring.name="SurvivorEnchantPulse";}
   else {entry=enchantPulses[enchantPulseCursor];enchantPulseCursor=(enchantPulseCursor+1)%EnchantPulseCapacity;}
   entry.Age=0;entry.Radius=radius;entry.Ring.transform.position=position+arcadeRoot.up*.045f;entry.Ring.transform.localScale=Vector3.one*.1f;
   EnemyView.ApplyEnchantColor(entry.Ring,index);entry.Ring.gameObject.SetActive(true);entry.Ring.enabled=true;
  }
  /// <summary>暂停、选卡和隐藏地图冻结年龄；源死亡不提前销毁已发波纹。</summary>
  private void TickEnchantPulses(float seconds,bool paused)
  {if(paused||arcadeRoot==null||!arcadeRoot.gameObject.activeInHierarchy)return;foreach(var e in enchantPulses){if(e.Ring==null||!e.Ring.enabled)continue;e.Age+=Mathf.Max(0,seconds);e.Ring.enabled=e.Age<EnchantPulseLifetime;e.Ring.transform.localScale=Vector3.one*Mathf.Lerp(.1f,e.Radius,Mathf.Clamp01(e.Age/EnchantPulseLifetime));}}
  /// <summary>End、Restart及地图卸载释放全部实例，借用资源不单独释放句柄。</summary>
  private void ClearEnchantPulses(){foreach(var e in enchantPulses)if(e.Ring!=null){e.Ring.gameObject.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(e.Ring.gameObject);else UnityEngine.Object.DestroyImmediate(e.Ring.gameObject);}enchantPulses.Clear();enchantPulseCursor=0;}
 }
}
