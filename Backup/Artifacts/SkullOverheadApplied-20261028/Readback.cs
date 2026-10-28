using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using UnityEngine;
using Game;
using Luban;

/// <summary>只读双端生成表并通过正式表现入口核对本机及远端不再创建头顶骷髅。</summary>
public static class SkullOverheadReadback
{
    public static string Run()
    {
        var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
        if(!provider.TryGetArcadeCatalog(out var catalog,out var error)) throw new Exception(error);
        var server = new TbArcadePowerup(new ByteBuf(File.ReadAllBytes("../Server/GameServer/Config/Luban/tbarcadepowerup.bytes")));
        var skulls = catalog.Powerups.Where(p => p.Behavior == ArcadePowerupBehavior.WeaponSkull).ToArray();
        if(skulls.Length != 2 || skulls.Any(p => !string.IsNullOrEmpty(p.ActiveVisualPrefabAddress))) throw new Exception("客户端仍有头顶模型引用");
        foreach(var skull in skulls)
            if(!string.IsNullOrEmpty(server.Get(skull.Id).ActiveVisualPrefabName)) throw new Exception("服务端仍有头顶模型引用");
        var view = new ArcadeCombatViewController();
        var root = new GameObject("TemporarySkullReadback");
        var flags = BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(ArcadeCombatViewController).GetField("root",flags).SetValue(view,root);
        try
        {
            foreach(long owner in new long[]{0,1,2})
            foreach(var skull in skulls)
            {
                view.BeginEffectSync(); view.SetPowerup(owner,skull,Vector2.zero); view.EndEffectSync();
                var entries = (IDictionary)typeof(ArcadeCombatViewController).GetField("effectVisuals",flags).GetValue(view);
                if(entries.Count != 0 || root.transform.childCount != 0) throw new Exception("仍创建头顶模型");
            }
            if(catalog.Drops.Count(d => d.ProductId.StartsWith("arcade.powerup.skull.") && !string.IsNullOrEmpty(d.VisualPrefabAddress)) != 2)
                throw new Exception("地面骷髅引用丢失");
            var result = "双端红紫生效模型引用为空；正式SetPowerup入口6次（本机0与玩家1/2、红/紫）均无视觉条目或子模型；地面两种掉落引用保留。";
            File.WriteAllText("../Backup/Artifacts/SkullOverheadApplied-20261028/readback.txt",result);
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
