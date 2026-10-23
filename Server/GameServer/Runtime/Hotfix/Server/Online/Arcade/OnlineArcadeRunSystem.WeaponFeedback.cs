namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>与单人一致，仅普通、速射和霰弹使用身体命中及普通墙面火星。</summary>
        private static bool HasBulletImpactFeedback(OnlineArcadeProjectileRuntime p)=>p.SourceWeaponId=="arcade.weapon.basic"||
            p.SourceWeaponId=="arcade.weapon.rapid"||p.SourceWeaponId=="arcade.weapon.spread";

        /// <summary>只在真实结算入口调用；穿透、喷火和无人机不增加单人没有的身体反馈。</summary>
        private static void RecordWeaponFeedback(OnlineArcadeBattleRuntime battle, OnlineArcadeProjectileRuntime p,
            int kind, float x, float z)
        {
            if(p.OwnerPlayerId<=0 || kind==1&&!HasBulletImpactFeedback(p))return;
            if(kind==3&&p.ExplosionRadius<=0)return;
            if(battle.WeaponFeedback.Count>=128)battle.WeaponFeedback.RemoveAt(0);
            battle.WeaponFeedback.Add(new OnlineArcadeWeaponFeedbackRuntime{
                Sequence=++battle.NextWeaponFeedbackSequence,Kind=kind,PositionX=x,PositionZ=z,
                DirectionX=p.VelocityX,DirectionZ=p.VelocityZ,Radius=kind>=3?p.ExplosionRadius:0,
                RoundGeneration=battle.RoundGeneration,ServerTime=TimeInfo.Instance.ServerNow()});
        }
    }
}
