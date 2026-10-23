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
            AppendWeaponFeedback(battle,kind,x,z,p.VelocityX,p.VelocityZ,kind>=3?p.ExplosionRadius:0);
        }

        /// <summary>诱饵及环绕桶在原结算点附加普通爆心事实，仍只由原入口扣次数、计算伤害。</summary>
        private static void DamageEnemiesFromSupportExplosion(this OnlineArcadeRunActor self,OnlineArcadeBattleRuntime battle,
            float x,float z,float radius,int damage,long ownerPlayerId)
        {
            if(ownerPlayerId>0&&radius>0)AppendWeaponFeedback(battle,3,x,z,0,0,radius);
            self.DamageEnemiesInExplosionRadius(battle,x,z,radius,damage,ownerPlayerId);
        }

        /// <summary>武器与支援爆炸共用同一有界序列，客户端沿用既有快照、时效及去重。</summary>
        private static void AppendWeaponFeedback(OnlineArcadeBattleRuntime battle,int kind,float x,float z,float directionX,float directionZ,float radius)
        {
            if(battle.WeaponFeedback.Count>=128)battle.WeaponFeedback.RemoveAt(0);
            battle.WeaponFeedback.Add(new OnlineArcadeWeaponFeedbackRuntime{
                Sequence=++battle.NextWeaponFeedbackSequence,Kind=kind,PositionX=x,PositionZ=z,
                DirectionX=directionX,DirectionZ=directionZ,Radius=radius,
                RoundGeneration=battle.RoundGeneration,ServerTime=TimeInfo.Instance.ServerNow()});
        }
    }
}
