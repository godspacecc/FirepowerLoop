using System;
using System.Linq;
using Game;
namespace ET.Server
{
    public static partial class OnlineTestComponentSystem
    {
        /// <summary>只执行玩家弓箭的具名联机验收，使用独立测试账号，不运行其他联机套件。</summary>
        private static async ETTask<int> RunWorldProjectiles(this OnlineTestComponent self)
        {
            int failed=0;
            failed+=await self.Case("WORLD-PROJECTILE-01 DamageWaitsForFlight",async()=>
            {
                using Session s=self.CreateSession();using var login=await self.RegisterAndLogin(s,UniqueUser("wp01"));using var entered=await s.Call(EnterWorld(Op(6001))) as G2C_EnterWorld;
                var enemy=entered.World.Enemies.First();var player=entered.World.Players.First(p=>p.UnitId==entered.World.SelfUnitId);
                await self.PrepareBow(s,entered.World,player.LifecycleRevision);
                using var moved=await s.Call(ReportWorldTransform(enemy.PositionX,enemy.PositionY,enemy.PositionZ-8,0,player.LifecycleRevision)) as G2C_ReportWorldTransform;
                using var shot=await s.Call(Arrow(1,player.LifecycleRevision,enemy.PositionX,enemy.PositionY+1,enemy.PositionZ-7.4f,0,0,1)) as G2C_WorldMeleeAttack;
                Ensure(shot.Accepted,"valid arrow rejected");using var early=await s.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(early.World.Enemies.First(e=>e.UnitId==enemy.UnitId).CurrentHP==enemy.CurrentHP,"damage happened at fire time");
                Ensure(early.World.Projectiles.Any(p=>p.OwnerUnitId==player.UnitId),"flying arrow absent from reconnect snapshot");
                await WaitCurrent(self,s,w=>w.Enemies.Any(e=>e.UnitId==enemy.UnitId&&e.CurrentHP==enemy.CurrentHP-10),1500,"arrival damage");
            });
            failed+=await self.Case("WORLD-PROJECTILE-02 RaisedShotDoesNotHitFlatTarget",async()=>
            {
                using Session s=self.CreateSession();using var login=await self.RegisterAndLogin(s,UniqueUser("wp02"));using var entered=await s.Call(EnterWorld(Op(6011))) as G2C_EnterWorld;
                var enemy=entered.World.Enemies.First();var player=entered.World.Players.First(p=>p.UnitId==entered.World.SelfUnitId);await self.PrepareBow(s,entered.World,player.LifecycleRevision);
                using var moved=await s.Call(ReportWorldTransform(enemy.PositionX,enemy.PositionY,enemy.PositionZ-8,0,player.LifecycleRevision)) as G2C_ReportWorldTransform;
                using var shot=await s.Call(Arrow(1,player.LifecycleRevision,enemy.PositionX,enemy.PositionY+1,enemy.PositionZ-7.4f,0,1,1)) as G2C_WorldMeleeAttack;Ensure(shot.Accepted,"raised arrow rejected");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(1100);using var current=await s.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(current.World.Enemies.Any(e=>e.UnitId==enemy.UnitId&&e.CurrentHP==enemy.CurrentHP),"vertical direction flattened");Ensure(current.World.Projectiles.All(p=>p.OwnerUnitId!=player.UnitId),"expired arrow remains");
            });
            failed+=await self.Case("WORLD-PROJECTILE-03 ForgedOriginAndHashConsumeNothing",async()=>
            {
                using Session s=self.CreateSession();using var login=await self.RegisterAndLogin(s,UniqueUser("wp03"));using var entered=await s.Call(EnterWorld(Op(6021))) as G2C_EnterWorld;
                var player=entered.World.Players.First(p=>p.UnitId==entered.World.SelfUnitId);await self.PrepareBow(s,entered.World,player.LifecycleRevision);
                var q=Arrow(1,player.LifecycleRevision,player.PositionX+20,player.PositionY+1,player.PositionZ,0,0,1);using var forged=await s.Call(q) as G2C_WorldMeleeAttack;Ensure(!forged.Accepted,"forged muzzle accepted");
                q=Arrow(2,player.LifecycleRevision,player.PositionX,player.PositionY+1,player.PositionZ+.6f,0,0,1);q.CollisionContentHash="wrong";using var wrongHash=await s.Call(q) as G2C_WorldMeleeAttack;Ensure(!wrongHash.Accepted,"wrong collision version accepted");
                using var current=await s.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(current.World.SelfInventory.Equipment.RangedLoadedAmmo==1,"invalid request consumed ammo");Ensure(current.World.Players.First(p=>p.UnitId==player.UnitId).LastAcceptedAttackSequence==0,"invalid request advanced sequence");
            });
            failed+=await self.Case("WORLD-PROJECTILE-04 DuplicateArrowAndLeaveCleanup",async()=>
            {
                using Session s=self.CreateSession();using var login=await self.RegisterAndLogin(s,UniqueUser("wp04"));using var entered=await s.Call(EnterWorld(Op(6031))) as G2C_EnterWorld;
                var p=entered.World.Players.First(x=>x.UnitId==entered.World.SelfUnitId);await self.PrepareBow(s,entered.World,p.LifecycleRevision);
                using Session observer=self.CreateSession();using var observerLogin=await self.RegisterAndLogin(observer,UniqueUser("wp04o"));using var observerEnter=await observer.Call(EnterWorld(Op(6032))) as G2C_EnterWorld;
                using var shot=await s.Call(Arrow(1,p.LifecycleRevision,p.PositionX,p.PositionY+1,p.PositionZ+.6f,0,1,.1f)) as G2C_WorldMeleeAttack;Ensure(shot.Accepted,"arrow rejected");
                using var duplicate=await s.Call(Arrow(1,p.LifecycleRevision,p.PositionX,p.PositionY+1,p.PositionZ+.6f,0,1,.1f)) as G2C_WorldMeleeAttack;Ensure(!duplicate.Accepted,"duplicate accepted");
                using var current=await s.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(current.World.Projectiles.Count(x=>x.OwnerUnitId==p.UnitId)==1,"duplicate created another arrow");
                using var leave=await s.Call(LeaveWorld(Op(6033))) as G2C_LeaveWorld;Ensure(leave.Error==0,"leave failed");using var after=await observer.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(after.World.Projectiles.All(x=>x.OwnerUnitId!=p.UnitId),"leaving player's arrow survives");
            });
            failed+=await self.Case("WORLD-PROJECTILE-05 DownShotRecoversOnGroundOnce",async()=>
            {
                using Session s=self.CreateSession();using var login=await self.RegisterAndLogin(s,UniqueUser("wp05"));using var entered=await s.Call(EnterWorld(Op(6041))) as G2C_EnterWorld;
                var p=entered.World.Players.First(x=>x.UnitId==entered.World.SelfUnitId);await self.PrepareBow(s,entered.World,p.LifecycleRevision);
                using var moved=await s.Call(ReportWorldTransform(-15,0,-15,0,p.LifecycleRevision)) as G2C_ReportWorldTransform;
                long[] old=entered.World.Corpses.Select(c=>c.CorpseId).ToArray();
                using var shot=await s.Call(Arrow(1,p.LifecycleRevision,-15,1,-15,0,-1,0)) as G2C_WorldMeleeAttack;Ensure(shot.Accepted,"down arrow rejected");
                await WaitCurrent(self,s,w=>w.Projectiles.All(a=>a.OwnerUnitId!=p.UnitId)&&w.Corpses.Any(c=>!old.Contains(c.CorpseId)&&c.PositionX==-15),800,"ground recovery");
                using var current=await s.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                var recovered=current.World.Corpses.Where(c=>!old.Contains(c.CorpseId)&&c.PositionX==-15).ToArray();Ensure(recovered.Length==1&&Math.Abs(recovered[0].PositionY)<.002f,"recovery duplicated or floating");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(100);using var after=await s.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(after.World.Corpses.Count(c=>!old.Contains(c.CorpseId)&&c.PositionX==-15)==1,"recovery duplicated after tick");
            });
            failed+=await self.Case("WORLD-PROJECTILE-06 WallStopsArrow",async()=>
            {
                using Session s=self.CreateSession();using var login=await self.RegisterAndLogin(s,UniqueUser("wp06"));using var entered=await s.Call(EnterWorld(Op(6051))) as G2C_EnterWorld;
                var p=entered.World.Players.First(x=>x.UnitId==entered.World.SelfUnitId);await self.PrepareBow(s,entered.World,p.LifecycleRevision);
                using var moved=await s.Call(ReportWorldTransform(18,0,-15,90,p.LifecycleRevision)) as G2C_ReportWorldTransform;
                long[] old=entered.World.Corpses.Select(c=>c.CorpseId).ToArray();
                using var shot=await s.Call(Arrow(1,p.LifecycleRevision,18.6f,1,-15,1,0,0)) as G2C_WorldMeleeAttack;Ensure(shot.Accepted,"wall shot rejected");
                await WaitCurrent(self,s,w=>w.Corpses.Any(c=>!old.Contains(c.CorpseId)&&c.PositionX>19&&c.PositionX<20),800,"wall contact recovery");
            });
            failed+=await self.Case("WORLD-PROJECTILE-07 WallBypassAndNonFiniteRejected",async()=>
            {
                using Session s=self.CreateSession();using var login=await self.RegisterAndLogin(s,UniqueUser("wp07"));using var entered=await s.Call(EnterWorld(Op(6061))) as G2C_EnterWorld;
                var p=entered.World.Players.First(x=>x.UnitId==entered.World.SelfUnitId);await self.PrepareBow(s,entered.World,p.LifecycleRevision);
                using var moved=await s.Call(ReportWorldTransform(19.8f,0,-15,90,p.LifecycleRevision)) as G2C_ReportWorldTransform;
                using var bypass=await s.Call(Arrow(1,p.LifecycleRevision,21.4f,1,-15,1,0,0)) as G2C_WorldMeleeAttack;Ensure(!bypass.Accepted,"wall bypass accepted");
                using var nan=await s.Call(Arrow(2,p.LifecycleRevision,19.8f,1,-15,0,float.NaN,1)) as G2C_WorldMeleeAttack;Ensure(!nan.Accepted,"NaN direction accepted");
                using var huge=await s.Call(Arrow(3,p.LifecycleRevision,19.8f,1,-15,float.MaxValue,0,1)) as G2C_WorldMeleeAttack;Ensure(!huge.Accepted,"overflow direction accepted");
                using var current=await s.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(current.World.SelfInventory.Equipment.RangedLoadedAmmo==1,"rejected requests consumed ammo");
            });
            failed+=await self.Case("WORLD-PROJECTILE-08 GraceReconnectRestoresFlyingArrow",async()=>
            {
                string user=UniqueUser("wp08");using Session s=self.CreateSession();using var login=await self.RegisterAndLogin(s,user);using var entered=await s.Call(EnterWorld(Op(6071))) as G2C_EnterWorld;
                var p=entered.World.Players.First(x=>x.UnitId==entered.World.SelfUnitId);await self.PrepareBow(s,entered.World,p.LifecycleRevision);
                using var shot=await s.Call(Arrow(1,p.LifecycleRevision,p.PositionX,p.PositionY+1,p.PositionZ+.6f,0,1,.1f)) as G2C_WorldMeleeAttack;Ensure(shot.Accepted,"arrow rejected");
                s.Dispose();await self.Root().GetComponent<TimerComponent>().WaitAsync(100);
                using Session reconnect=self.CreateSession();using var relogin=await reconnect.Call(Login(user)) as G2C_Login;using var current=await reconnect.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(relogin.IsPlayerActorReused&&current.World.SelfUnitId==p.UnitId,"grace identity lost");Ensure(current.World.Projectiles.Any(a=>a.OwnerUnitId==p.UnitId&&a.DirectionY>0),"disconnect removed accepted arrow");
            });
            return failed;
        }
        private static async ETTask PrepareBow(this OnlineTestComponent self,Session session,OnlineWorldSnapshot world,long lifecycle)
        {
            using var active=await session.Call(InventoryOperation(Op(6100),world.SelfInventory.Revision,0,0,7,0,0,0,0,lifecycle)) as G2C_OperateWorldInventory;Ensure(active.Accepted,"activate bow failed");
            using var reload=await session.Call(InventoryOperation(Op(6101),active.SelfInventory.Revision,0,0,8,0,0,0,0,lifecycle)) as G2C_OperateWorldInventory;Ensure(reload.Accepted,"reload bow failed");
        }
        private static C2G_WorldMeleeAttack Arrow(long sequence,long lifecycle,float x,float y,float z,float dx,float dy,float dz)
        {
            var catalog=new OnlineWorldCollisionCatalog();Ensure(catalog.TryGet(MainWorldMapId,out var collision,out var error),error);
            var q=Melee(sequence,6,dx,dz,lifecycle);q.AimY=dy;q.OriginX=x;q.OriginY=y;q.OriginZ=z;q.HasProjectileOrigin=true;q.CollisionContentHash=collision.ContentHash;return q;
        }
    }
}
