using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>金币独立账本与活动局领取水位；不保存现场物体。</summary>
        private SurvivorGoldFileStore goldStore;
        private string goldRunToken;
        private long goldSequence;
        /// <summary>每个地面拾取物严格对应一金币，与经验预算独立。</summary>
        private readonly Dictionary<int,Vector2> goldDrops=new();
        private readonly System.Random goldRandom=new System.Random();
        private float goldRetryRemaining;
        private ArcadeDropDefinition goldVisual;
        /// <summary>入口读取持有金币；坏档明确失败，不覆盖原余额。</summary>
        public bool TryReadGold(out long balance,out string error)
        {
            goldStore??=new SurvivorGoldFileStore(1);
            if(!goldStore.TryRead(out balance,out error))return false;
            Model.GoldBalance=balance;return true;
        }
        /// <summary>正式新局启用独立令牌，旧局领取失效；累计余额保持。</summary>
        private bool BeginGoldRun(out string error)
        {
            goldRunToken=Guid.NewGuid().ToString("N");goldSequence=0;goldRetryRemaining=0;
            goldStore??=new SurvivorGoldFileStore(1);
            if(!goldStore.TryBeginRun(goldRunToken,out var balance,out error)){goldRunToken=null;return false;}
            Model.RunGold=0;Model.GoldBalance=balance;
            var source=catalog.Drops.Single(d=>d.Id=="arcade.drop.gold.small");
            goldVisual=new ArcadeDropDefinition(source.Id,source.MinArena,source.MaxArena,source.Weight,source.Kind,source.ProductId,source.VisualPrefabAddress,1,0,0);
            return true;
        }
        /// <summary>沿用敌人强度掉率，成功生成一份金币；预算满时不创建不可显示的金币。</summary>
        private void TryDropGold(ArcadeEnemyRuntime enemy)
        {
            if(goldDrops.Count>=config.MaxDrops)return;
            var r=catalog.Rule;
            if(goldRandom.NextDouble()>=ArcadeEnemyDropRules.Chance(r.DropChance,enemy.MaxHealth,enemy.Damage,r.EnemyDropHealthUnit,r.EnemyDropDamageUnit,r.EnemyDropMaxChance))return;
            goldDrops.Add(++identity,enemy.Position);
        }
        /// <summary>保存成功才消除拾取物；失败保留金币，限频重试并沿现有提示展示。</summary>
        private void TickGold(float dt)
        {
            goldRetryRemaining=Math.Max(0,goldRetryRemaining-dt);
            foreach(var pair in goldDrops.ToArray())
            {
                view.SetDrop(pair.Key,goldVisual,pair.Value);
                if(goldRetryRemaining>0||Vector2.Distance(player,pair.Value)>config.PickupRadius+Bonus("Pickup"))continue;
                if(!goldStore.TryCollect(goldRunToken+":"+(goldSequence+1),out var balance,out var error))
                {
                    if(LastError!=error)Notify.ShowFloatTips(error);
                    LastError=error;goldRetryRemaining=2;break;
                }
                goldSequence++;Model.RunGold++;Model.GoldBalance=balance;LastError=null;
                goldDrops.Remove(pair.Key);view.RemoveDrop(pair.Key);
            }
        }
    }
}
