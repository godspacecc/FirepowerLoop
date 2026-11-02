using UnityEngine;
namespace Game
{
 public sealed partial class EnemyView
 {
  /// <summary>随敌人Prefab预载的圆环模板，依赖共享材质由原资源句柄持有。</summary>
  [SerializeField] private LineRenderer survivorEnchantRingTemplate;
  public LineRenderer SurvivorEnchantRingTemplate=>survivorEnchantRingTemplate;
  /// <summary>每个实例最多三条标识；只反映完整快照，不保存权威层数。</summary>
  private LineRenderer[] enchantReceived;
  /// <summary>完整投影可重复调用；盾蓝环只取剩余附魔盾，不控制原生装备。</summary>
  public void SetSurvivorEnchantment(SurvivorEnchantSnapshot state)
  {
   if(survivorEnchantRingTemplate==null)return;
   // 普通零快照不创建对象，密集尸潮只为实际获得附魔的目标分配三条环。
   if(enchantReceived==null&&state.HealthStacks<=0&&state.SpeedStacks<=0&&state.ShieldRemaining<=0)return;
   if(enchantReceived==null)
   {
    enchantReceived=new LineRenderer[3];
    for(int i=0;i<3;i++)
    {
     var ring=Instantiate(survivorEnchantRingTemplate,transform,false);ring.name="EnchantReceived"+i;
     ring.transform.localPosition=new Vector3(0,.035f+i*.008f,0);ring.transform.localScale=Vector3.one*(.48f+i*.12f);
     ApplyEnchantColor(ring,i);ring.gameObject.SetActive(true);enchantReceived[i]=ring;
    }
   }
   enchantReceived[0].enabled=state.HealthStacks>0;enchantReceived[1].enabled=state.SpeedStacks>0;enchantReceived[2].enabled=state.ShieldRemaining>0;
  }
  /// <summary>与采用预览一致的生命红、迅捷绿、附魔盾蓝。</summary>
  internal static Color SurvivorEnchantColor(int kind)=>kind==0?new Color(.8f,.14f,.22f):kind==1?new Color(.12f,.65f,.25f):new Color(.15f,.4f,.95f);
  /// <summary>URP无光照材质通过属性块染色，避免修改共享白色模板。</summary>
  internal static void ApplyEnchantColor(LineRenderer ring,int kind)
  {var block=new MaterialPropertyBlock();block.SetColor("_BaseColor",SurvivorEnchantColor(kind));ring.SetPropertyBlock(block);ring.startColor=ring.endColor=Color.white;}
  /// <summary>死亡、休眠、回池及新用途统一隐藏，不反写规则。</summary>
  private void ResetSurvivorEnchantment(){if(enchantReceived!=null)foreach(var ring in enchantReceived)if(ring!=null)ring.enabled=false;}
 }
}
