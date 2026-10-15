using System;
using System.Collections.Generic;
using System.Linq;

namespace ET.Server
{
    [FriendOf(typeof(OnlineTestComponent))]
    public static partial class OnlineTestComponentSystem
    {
        private const long MainWorldId = 1;
        private const string MainWorldMapId = "map.another-day.survival-sample";
        private const string MissionWorldMapId = "map.another-day.mission-sample";
        private const string VillageWorldMapId = "map.hejia-village";

        private static async ETTask<int> RunWorld(this OnlineTestComponent self)
        {
            int failed = 0;
            failed += await self.Case("WORLD-01 MainWorldIdentityAndIndependentEntry", async () =>
            {
                using Session first = self.CreateSession();
                using G2C_Login firstLogin = await self.RegisterAndLogin(first, UniqueUser("w01a"));
                using G2C_EnterWorld firstEnter = await first.Call(EnterWorld(Op(3001))) as G2C_EnterWorld;
                using Session second = self.CreateSession();
                using G2C_Login secondLogin = await self.RegisterAndLogin(second, UniqueUser("w01b"));
                using G2C_EnterWorld secondEnter = await second.Call(EnterWorld(Op(3002))) as G2C_EnterWorld;
                Ensure(firstEnter.Error == 0 && secondEnter.Error == 0 && firstEnter.HasWorld && secondEnter.HasWorld, "independent enter failed");
                Ensure(firstEnter.World.WorldId == MainWorldId && secondEnter.World.WorldId == MainWorldId && firstEnter.World.MapId == MainWorldMapId && secondEnter.World.MapId == MainWorldMapId, "main world identity mismatch");
                Ensure(firstEnter.World.SelfUnitId > 0 && secondEnter.World.SelfUnitId > 0 && firstEnter.World.SelfUnitId != secondEnter.World.SelfUnitId, "unit ids are not unique");
                WorldPlayerInfo firstInfo = secondEnter.World.Players.First(x => x.PlayerId == firstLogin.PlayerId);
                WorldPlayerInfo secondInfo = secondEnter.World.Players.First(x => x.PlayerId == secondLogin.PlayerId);
                Ensure(firstInfo.PositionX == 0f && secondInfo.PositionX == 2f && firstInfo.PositionY == .1f && secondInfo.PositionY == .1f, "spawn grid mismatch");
            });

            failed += await self.Case("WORLD-02 RoomMembersEnterIndependentlyAndLastLeaveClosesRoom", async () =>
            {
                using Session owner = self.CreateSession();
                using G2C_Login ownerLogin = await self.RegisterAndLogin(owner, UniqueUser("w02a"));
                using G2C_CreateRoom created = await owner.Call(CreateRoom(Op(3010))) as G2C_CreateRoom;
                using Session member = self.CreateSession();
                using G2C_Login memberLogin = await self.RegisterAndLogin(member, UniqueUser("w02b"));
                using G2C_JoinRoom joined = await member.Call(JoinRoom(Op(3011), created.Room.RoomCode)) as G2C_JoinRoom;
                using G2C_EnterWorld ownerEnter = await owner.Call(EnterWorld(Op(3012))) as G2C_EnterWorld;
                using G2C_GetCurrentRoom memberRoom = await member.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(ownerEnter.Error == 0 && !memberRoom.Room.Members.Any(x => x.PlayerId == ownerLogin.PlayerId) && memberRoom.Room.Members.Any(x => x.PlayerId == memberLogin.PlayerId), "first member did not leave independently");
                using G2C_EnterWorld memberEnter = await member.Call(EnterWorld(Op(3013))) as G2C_EnterWorld;
                using G2C_GetCurrentRoom noRoom = await member.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(memberEnter.Error == 0 && !noRoom.HasRoom, $"last room member was not detached: {noRoom.Error}");
            });

            failed += await self.Case("WORLD-03 DuplicateEnterReturnsSameUnit", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login _ = await self.RegisterAndLogin(session, UniqueUser("w03"));
                using G2C_EnterWorld first = await session.Call(EnterWorld(Op(3020))) as G2C_EnterWorld;
                using G2C_EnterWorld second = await session.Call(EnterWorld(Op(3021))) as G2C_EnterWorld;
                Ensure(first.Error == 0 && second.Error == 0 && first.World.SelfUnitId == second.World.SelfUnitId, "duplicate enter changed unit");
            });

            failed += await self.Case("WORLD-04 TransformRelaysToOtherPlayer", async () =>
            {
                using Session sender = self.CreateSession(); using G2C_Login _ = await self.RegisterAndLogin(sender, UniqueUser("w04a"));
                using G2C_EnterWorld senderEnter = await sender.Call(EnterWorld(Op(3030))) as G2C_EnterWorld;
                using Session observer = self.CreateSession(); using G2C_Login __ = await self.RegisterAndLogin(observer, UniqueUser("w04b"));
                using G2C_EnterWorld observerEnter = await observer.Call(EnterWorld(Op(3031))) as G2C_EnterWorld;
                int before = self.WorldTransformSessionIds.Count;
                long lifecycle=senderEnter.World.Players.First(x=>x.UnitId==senderEnter.World.SelfUnitId).LifecycleRevision;
                using G2C_ReportWorldTransform report = await sender.Call(ReportWorldTransform(17f, .25f, -8f, 271f,lifecycle)) as G2C_ReportWorldTransform;
                await self.WaitForWorldPush(() => self.WorldTransformSessionIds.Count > before, 2000, "transform push");
                int index = self.WorldTransformSessionIds.Count - 1;
                Ensure(report.Error == 0 && self.WorldTransformSessionIds[index] == observer.Id && self.WorldTransformPlayers[index].UnitId == senderEnter.World.SelfUnitId, "transform was not relayed only to observer");
                Ensure(self.WorldTransformPlayers[index].PositionX == 17f && self.WorldTransformPlayers[index].YawDegrees == 271f && report.StateRevision == self.WorldTransformPlayers[index].StateRevision, "transform payload mismatch");
            });

            failed += await self.Case("WORLD-05 GraceReconnectKeepsUnitAndTimeoutRemovesIt", async () =>
            {
                string user = UniqueUser("w05a");
                Session first = self.CreateSession(); using G2C_Login _ = await self.RegisterAndLogin(first, user); using G2C_EnterWorld entered = await first.Call(EnterWorld(Op(3040))) as G2C_EnterWorld;
                using Session observer = self.CreateSession(); using G2C_Login __ = await self.RegisterAndLogin(observer, UniqueUser("w05b")); using G2C_EnterWorld ___ = await observer.Call(EnterWorld(Op(3041))) as G2C_EnterWorld;
                first.Dispose(); await self.Root().GetComponent<TimerComponent>().WaitAsync(250);
                using Session reconnect = self.CreateSession(); using G2C_Login login = await reconnect.Call(Login(user)) as G2C_Login; using G2C_GetCurrentWorld current = await reconnect.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(login.Error == 0 && login.IsPlayerActorReused && current.HasWorld && current.World.SelfUnitId == entered.World.SelfUnitId, "grace reconnect did not preserve unit");
                int before = self.WorldLeftSessionIds.Count; reconnect.Dispose();
                await self.WaitForWorldPush(() => self.WorldLeftSessionIds.Count > before, 4000, "timeout left push");
                Ensure(self.WorldLeftSessionIds.Last() == observer.Id && self.WorldLeftUnitIds.Last() == entered.World.SelfUnitId, "timeout did not remove unit for observer");
            });

            failed += await self.Case("WORLD-06 ExplicitLeaveAndLogoutRemoveUnit", async () =>
            {
                using Session observer = self.CreateSession(); using G2C_Login _ = await self.RegisterAndLogin(observer, UniqueUser("w06o")); using G2C_EnterWorld __ = await observer.Call(EnterWorld(Op(3050))) as G2C_EnterWorld;
                using Session explicitSession = self.CreateSession(); using G2C_Login ___ = await self.RegisterAndLogin(explicitSession, UniqueUser("w06e")); using G2C_EnterWorld explicitEnter = await explicitSession.Call(EnterWorld(Op(3051))) as G2C_EnterWorld;
                int before = self.WorldLeftSessionIds.Count; using G2C_LeaveWorld leave = await explicitSession.Call(LeaveWorld(Op(3052))) as G2C_LeaveWorld; await self.WaitForWorldPush(() => self.WorldLeftSessionIds.Count > before, 2000, "explicit left push");
                Ensure(leave.Error == 0 && self.WorldLeftUnitIds.Last() == explicitEnter.World.SelfUnitId, "explicit leave failed");
                using Session logoutSession = self.CreateSession(); using G2C_Login ____ = await self.RegisterAndLogin(logoutSession, UniqueUser("w06l")); using G2C_EnterWorld logoutEnter = await logoutSession.Call(EnterWorld(Op(3053))) as G2C_EnterWorld;
                before = self.WorldLeftSessionIds.Count; using G2C_Logout logout = await logoutSession.Call(C2G_Logout.Create()) as G2C_Logout; await self.WaitForWorldPush(() => self.WorldLeftSessionIds.Count > before, 2000, "logout left push");
                Ensure(logout.Error == 0 && self.WorldLeftUnitIds.Last() == logoutEnter.World.SelfUnitId, "logout did not remove unit");
            });
            return failed;
        }

        private static async ETTask<int> RunWorldCombat(this OnlineTestComponent self)
        {
            if ((Options.Instance.OnlineTestUser??string.Empty).StartsWith("world-inventory-", StringComparison.Ordinal))
                return await self.RunWorldInventory();
            int failed=0;Session first=null;Session second=null;Session third=null;G2C_EnterWorld firstEnter=null;G2C_EnterWorld secondEnter=null;G2C_EnterWorld thirdEnter=null;string firstUser=UniqueUser("w08a");long firstUnit=0;long firstPlayerId=0;
            failed+=await self.Case("WORLD-08 SharedFourMeleeEnemies",async()=>
            {
                Ensure(Options.Instance.OnlineWorldTickMilliseconds==100&&Options.Instance.OnlineWorldPlayerRespawnMilliseconds==3000&&Options.Instance.OnlineWorldNavigationHeartbeatMilliseconds==500&&Options.Instance.OnlineWorldNavigationLeaseTimeoutMilliseconds==2000,"production timing defaults changed");
                first=self.CreateSession();using G2C_Login firstLogin=await self.RegisterAndLogin(first,firstUser);firstPlayerId=firstLogin.PlayerId;firstEnter=await first.Call(EnterWorld(Op(3080))) as G2C_EnterWorld;firstUnit=firstEnter.World.SelfUnitId;
                second=self.CreateSession();using G2C_Login __=await self.RegisterAndLogin(second,UniqueUser("w08b"));secondEnter=await second.Call(EnterWorld(Op(3081))) as G2C_EnterWorld;
                Ensure(firstEnter.World.Enemies.Count==4&&secondEnter.World.Enemies.Count==4,"expected four enemies");Ensure(firstEnter.World.Enemies.Select(x=>x.UnitId).SequenceEqual(secondEnter.World.Enemies.Select(x=>x.UnitId)),"enemy identities differ");Ensure(firstEnter.World.Enemies.All(x=>x.EnemyConfigId=="enemy.graybox.basic"&&x.MaxHP==20),"non-melee enemy entered snapshot");Ensure(firstEnter.World.NavigationExecutorUnitId==firstEnter.World.SelfUnitId,"smallest online unit did not receive lease");
            });
            failed+=await self.Case("WORLD-09 AuthoritativeMeleeAndIdempotency",async()=>
            {
                using G2C_GetCurrentWorld start=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldPlayerInfo p=start.World.Players.First(x=>x.UnitId==start.World.SelfUnitId);WorldEnemyInfo a=start.World.Enemies[0],b=start.World.Enemies[1];int secondPlayerHp=start.World.Players.First(x=>x.UnitId==secondEnter.World.SelfUnitId).CurrentHP;
                using G2C_ReportWorldTransform nearA=await first.Call(ReportWorldTransform(a.PositionX,a.PositionY,a.PositionZ-1f,0,p.LifecycleRevision)) as G2C_ReportWorldTransform;
                using G2C_OperateWorldInventory emptyHands=await first.Call(InventoryOperation(Op(30900),start.World.SelfInventory.Revision,0,0,3,1,0,1,0,p.LifecycleRevision)) as G2C_OperateWorldInventory;Ensure(emptyHands.Accepted,"holster before punch failed");
                using G2C_WorldMeleeAttack punch=await first.Call(Melee(1,0,0,1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(punch.Accepted,"unarmed attack rejected");
                using G2C_GetCurrentWorld afterPunch=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(afterPunch.World.Enemies.First(x=>x.UnitId==a.UnitId).CurrentHP==a.CurrentHP-3&&afterPunch.World.Enemies.Where(x=>x.UnitId!=a.UnitId).All(x=>x.CurrentHP==20),"unarmed attack was not nearest single target");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(650);
                long navSequence=100;for(int step=0;step<3;step++){using G2C_GetCurrentWorld c=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldEnemyInfo ca=c.World.Enemies.First(x=>x.UnitId==a.UnitId),cb=c.World.Enemies.First(x=>x.UnitId==b.UnitId);var reports=new List<WorldEnemyNavigationReport>{NavToward(ca,-10.6f,ca.PositionY,-0.5f,1.2f),NavToward(cb,-9.4f,cb.PositionY,-0.5f,1.2f)};Ensure(await ReportNavigationCurrent(self,first,second,firstUnit,secondEnter.World.SelfUnitId,reports),"cluster navigation rejected");navSequence++;await self.Root().GetComponent<TimerComponent>().WaitAsync(120);}
                using G2C_GetCurrentWorld heldBefore=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;using G2C_OperateWorldInventory drawSword=await first.Call(InventoryOperation(Op(30901),heldBefore.World.SelfInventory.Revision,0,0,2,1,0,1,0,p.LifecycleRevision)) as G2C_OperateWorldInventory;Ensure(drawSword.Accepted,"draw sword failed");
                using G2C_ReportWorldTransform center=await first.Call(ReportWorldTransform(-10f,1f,-2f,0,p.LifecycleRevision)) as G2C_ReportWorldTransform;using G2C_WorldMeleeAttack sword=await first.Call(Melee(2,5,0,1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(sword.Accepted,"sword attack rejected");using G2C_GetCurrentWorld afterSword=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(afterSword.World.Enemies.First(x=>x.UnitId==a.UnitId).CurrentHP==7&&afterSword.World.Enemies.First(x=>x.UnitId==b.UnitId).CurrentHP==10,"sword did not hit both targets");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(500);using G2C_WorldMeleeAttack whiff=await first.Call(Melee(3,5,0,-1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(whiff.Accepted,"legal whiff rejected");using G2C_WorldMeleeAttack cooldown=await first.Call(Melee(4,5,0,-1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(!cooldown.Accepted,"whiff did not consume cooldown");await self.Root().GetComponent<TimerComponent>().WaitAsync(500);using G2C_WorldMeleeAttack duplicate=await first.Call(Melee(3,5,0,-1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(!duplicate.Accepted,"duplicate sequence accepted");using G2C_WorldMeleeAttack invalid=await first.Call(Melee(4,5,float.NaN,1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(!invalid.Accepted,"invalid aim accepted");
                using G2C_GetCurrentWorld beforeRange=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Dictionary<long,int> hpBefore=beforeRange.World.Enemies.ToDictionary(x=>x.UnitId,x=>x.CurrentHP);using G2C_ReportWorldTransform far=await first.Call(ReportWorldTransform(80f,1f,80f,0,p.LifecycleRevision)) as G2C_ReportWorldTransform;using G2C_WorldMeleeAttack outOfRange=await first.Call(Melee(4,5,0,1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(outOfRange.Accepted,"out-of-range legal attack rejected");using G2C_WorldMeleeAttack descending=await first.Call(Melee(2,5,0,1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(!descending.Accepted,"descending attack sequence accepted");using G2C_GetCurrentWorld final=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(final.World.Enemies.All(x=>hpBefore[x.UnitId]==x.CurrentHP),"descending or out-of-range attack caused damage");Ensure(final.World.Players.First(x=>x.UnitId==secondEnter.World.SelfUnitId).CurrentHP==secondPlayerHp,"player friendly fire occurred");using G2C_ReportWorldTransform restore=await first.Call(ReportWorldTransform(-10f,1f,-2f,0,p.LifecycleRevision)) as G2C_ReportWorldTransform;
            });
            failed+=await self.Case("WORLD-10 EnemyDamageDeathRespawnAndOldLifecycle",async()=>
            {
                using G2C_GetCurrentWorld before=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                WorldPlayerInfo p=before.World.Players.First(x=>x.UnitId==before.World.SelfUnitId);long oldUnit=firstUnit;WorldEnemyInfo a=before.World.Enemies[0],b=before.World.Enemies[1];
                var reports=new List<WorldEnemyNavigationReport>{Nav(a,p.PositionX-.4f,p.PositionY,p.PositionZ+.2f,a.LifecycleRevision),Nav(b,p.PositionX+.4f,p.PositionY,p.PositionZ+.2f,b.LifecycleRevision)};
                Ensure(await ReportNavigationCurrent(self,first,second,firstUnit,secondEnter.World.SelfUnitId,reports),"enemy approach rejected");
                await WaitCurrent(self,first,x=>x.SelfUnitId==0&&x.Corpses.Any(y=>y.SourceUnitId==oldUnit&&y.SourcePlayerId==firstPlayerId),7000,"old unit to zero death transition");
                await WaitCurrent(self,first,x=>x.SelfUnitId!=0&&x.SelfUnitId!=oldUnit&&x.Players.Any(y=>y.UnitId==x.SelfUnitId&&!y.IsDead&&y.CurrentHP==y.MaxHP),1500,"zero to new unit respawn transition");
                using G2C_GetCurrentWorld respawned=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldPlayerInfo alive=respawned.World.Players.First(x=>x.UnitId==respawned.World.SelfUnitId);WorldCorpseInfo own=respawned.World.Corpses.Single(x=>x.SourceUnitId==oldUnit&&x.SourcePlayerId==firstPlayerId);
                Ensure(respawned.World.SelfInventory.Slots.Count==0&&respawned.World.SelfInventory.Equipment.MeleeWeaponItemId==0,"new character did not receive empty online inventory");Ensure(alive.CurrentHP==alive.MaxHP&&alive.PositionX==0f&&alive.PositionY==.1f&&alive.PositionZ==-14f,"new character did not respawn full at configured spawn");
                using C2G_ExecuteServerTestCmd staleCommand=C2G_ExecuteServerTestCmd.Create();staleCommand.CommandText=$"world-old-unit-rejected {oldUnit} {p.LifecycleRevision}";using G2C_ExecuteServerTestCmd staleResult=await first.Call(staleCommand) as G2C_ExecuteServerTestCmd;Ensure(staleResult.Error==0&&staleResult.Recognized&&staleResult.Success,$"old unit movement, attack, or inventory request was accepted: {staleResult.Message}");
                using G2C_ReportWorldTransform nearOwn=await first.Call(ReportWorldTransform(own.PositionX,own.PositionY,own.PositionZ,0,alive.LifecycleRevision)) as G2C_ReportWorldTransform;using G2C_OpenWorldCorpse opened=await first.Call(OpenCorpse(own.CorpseId,alive.LifecycleRevision)) as G2C_OpenWorldCorpse;Ensure(opened.Error==0&&opened.CorpseContents.Slots.Count>0,"respawned player could not open own corpse");firstUnit=respawned.World.SelfUnitId;
            });
            failed+=await self.Case("WORLD-11 EnemyDeathRemovesActiveEnemy",async()=>
            {
                using G2C_GetCurrentWorld c=await second.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldPlayerInfo p=c.World.Players.First(x=>x.UnitId==c.World.SelfUnitId);WorldEnemyInfo e=c.World.Enemies[2];using G2C_ReportWorldTransform move=await second.Call(ReportWorldTransform(e.PositionX,e.PositionY,e.PositionZ-1,0,p.LifecycleRevision)) as G2C_ReportWorldTransform;using G2C_WorldMeleeAttack a1=await second.Call(Melee(1,5,0,1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;await self.Root().GetComponent<TimerComponent>().WaitAsync(500);using G2C_WorldMeleeAttack a2=await second.Call(Melee(2,5,0,1,p.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(a1.Accepted&&a2.Accepted,"enemy kill attacks rejected");using G2C_GetCurrentWorld dead=await second.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(dead.World.Enemies.All(x=>x.UnitId!=e.UnitId),"dead enemy remained in active enemy snapshot");WorldCorpseInfo corpse=dead.World.Corpses.Single(x=>x.SourceUnitId==e.UnitId);Ensure(await ReportNavigationCurrent(self,first,second,firstUnit,secondEnter.World.SelfUnitId,new[]{Nav(e,99,e.PositionY,99,e.LifecycleRevision)}),"removed enemy navigation entry should be isolated, not reject batch");await self.Root().GetComponent<TimerComponent>().WaitAsync(1000);using G2C_GetCurrentWorld later=await second.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(later.World.Enemies.All(x=>x.UnitId!=e.UnitId)&&later.World.Corpses.Any(x=>x.CorpseId==corpse.CorpseId),"dead enemy reappeared or its independent corpse was lost");
            });
            failed+=await self.Case("WORLD-12 NavigationLeaseTransfers",async()=>
            {
                first?.Dispose();first=null;second?.Dispose();second=null;
                string alphaUser=UniqueUser("w12a"),betaUser=UniqueUser("w12b");
                using Session alpha=self.CreateSession();using Session beta=self.CreateSession();using G2C_Login alphaLogin=await self.RegisterAndLogin(alpha,alphaUser);using G2C_Login betaLogin=await self.RegisterAndLogin(beta,betaUser);using G2C_EnterWorld alphaEnter=await alpha.Call(EnterWorld(Op(3082))) as G2C_EnterWorld;using G2C_EnterWorld betaEnter=await beta.Call(EnterWorld(Op(3083))) as G2C_EnterWorld;
                long alphaUnit=alphaEnter.World.SelfUnitId,betaUnit=betaEnter.World.SelfUnitId;
                await WaitCurrent(self,alpha,x=>x.Players.Any(y=>y.UnitId==alphaUnit&&y.ConnectionState==1)&&x.Players.Any(y=>y.UnitId==betaUnit&&y.ConnectionState==1)&&(x.NavigationExecutorUnitId==alphaUnit||x.NavigationExecutorUnitId==betaUnit),1000,"two explicit online navigation candidates");
                using G2C_GetCurrentWorld initial=await alpha.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;long executorUnit=initial.World.NavigationExecutorUnitId;long candidateUnit=executorUnit==alphaUnit?betaUnit:alphaUnit;Session executor=executorUnit==alphaUnit?alpha:beta;Session candidate=executorUnit==alphaUnit?beta:alpha;string executorUser=executorUnit==alphaUnit?alphaUser:betaUser;
                long lease=initial.World.NavigationLeaseRevision;using G2C_ReportWorldEnemyNavigation heartbeat=await executor.Call(Navigation(lease,1,Array.Empty<WorldEnemyNavigationReport>())) as G2C_ReportWorldEnemyNavigation;Ensure(heartbeat.Accepted,"explicit executor heartbeat rejected");
                await WaitCurrent(self,candidate,x=>x.NavigationExecutorUnitId==candidateUnit&&x.NavigationLeaseRevision>lease,1200,"explicit heartbeat-timeout transfer");using G2C_GetCurrentWorld transferred=await candidate.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                using G2C_ReportWorldEnemyNavigation stale=await executor.Call(Navigation(lease,2,Array.Empty<WorldEnemyNavigationReport>())) as G2C_ReportWorldEnemyNavigation;Ensure(!stale.Accepted,"old executor lease remained usable after transfer");using G2C_ReportWorldEnemyNavigation current=await candidate.Call(Navigation(transferred.World.NavigationLeaseRevision,1,Array.Empty<WorldEnemyNavigationReport>())) as G2C_ReportWorldEnemyNavigation;Ensure(current.Accepted,"transferred executor lease unusable");
                executor.Dispose();await WaitCurrent(self,candidate,x=>x.Players.Any(y=>y.UnitId==executorUnit&&y.ConnectionState==2)&&x.NavigationExecutorUnitId==candidateUnit,1000,"timed-out executor disconnect");using Session reconnect=self.CreateSession();using G2C_Login recovered=await reconnect.Call(Login(executorUser)) as G2C_Login;Ensure(recovered.Error==0&&recovered.IsPlayerActorReused,"timed-out executor reconnect failed");await WaitCurrent(self,reconnect,x=>x.SelfUnitId==executorUnit&&x.NavigationExecutorUnitId==executorUnit,1000,"reconnected executor eligibility recovery");using G2C_GetCurrentWorld recoveredWorld=await reconnect.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;using G2C_ReportWorldEnemyNavigation recoveredHeartbeat=await reconnect.Call(Navigation(recoveredWorld.World.NavigationLeaseRevision,1,Array.Empty<WorldEnemyNavigationReport>())) as G2C_ReportWorldEnemyNavigation;Ensure(recoveredHeartbeat.Accepted,"reconnected executor lease unusable");
            });
            failed+=await self.Case("WORLD-13 GraceStateContinues",async()=>
            {
                string navigatorUser=UniqueUser("w13n"),targetUser=UniqueUser("w13t");
                using Session navigator=self.CreateSession();Session target=self.CreateSession();using G2C_Login navigatorLogin=await self.RegisterAndLogin(navigator,navigatorUser);using G2C_Login targetLogin=await self.RegisterAndLogin(target,targetUser);using G2C_EnterWorld navigatorEnter=await navigator.Call(EnterWorld(Op(3084))) as G2C_EnterWorld;using G2C_EnterWorld targetEnter=await target.Call(EnterWorld(Op(3085))) as G2C_EnterWorld;long navigatorUnit=navigatorEnter.World.SelfUnitId,targetUnit=targetEnter.World.SelfUnitId,targetPlayerId=targetLogin.PlayerId;
                await WaitCurrent(self,navigator,x=>x.NavigationExecutorUnitId==navigatorUnit&&x.Players.Any(y=>y.UnitId==navigatorUnit&&y.ConnectionState==1)&&x.Players.Any(y=>y.UnitId==targetUnit&&y.ConnectionState==1),1000,"independent grace test online precondition");
                using G2C_GetCurrentWorld start=await navigator.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldPlayerInfo navigatorPlayer=start.World.Players.First(x=>x.UnitId==navigatorUnit);WorldPlayerInfo targetPlayer=start.World.Players.First(x=>x.UnitId==targetUnit);using G2C_ReportWorldTransform moveNavigator=await navigator.Call(ReportWorldTransform(90f,1f,90f,0,navigatorPlayer.LifecycleRevision)) as G2C_ReportWorldTransform;using G2C_ReportWorldTransform holdTarget=await target.Call(ReportWorldTransform(80f,1f,80f,0,targetPlayer.LifecycleRevision)) as G2C_ReportWorldTransform;
                for(int step=0;step<20;step++){using G2C_GetCurrentWorld c=await navigator.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(await ReportNavigationCurrent(self,navigator,target,navigatorUnit,targetUnit,c.World.Enemies.Select(e=>NavToward(e,-10f,e.PositionY,0f,1.2f)).ToList()),"independent enemy staging rejected");await self.Root().GetComponent<TimerComponent>().WaitAsync(30);}
                using G2C_ReportWorldTransform exposeTarget=await target.Call(ReportWorldTransform(-10f,1f,-2.5f,0,targetPlayer.LifecycleRevision)) as G2C_ReportWorldTransform;bool onlineInjured=false;long injuryDeadline=TimeInfo.Instance.ClientNow()+2500;while(TimeInfo.Instance.ClientNow()<injuryDeadline){using G2C_GetCurrentWorld c=await navigator.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldPlayerInfo y=c.World.Players.First(z=>z.UnitId==targetUnit);if(y.ConnectionState==1&&!y.IsDead&&y.CurrentHP>0&&y.CurrentHP<=20){onlineInjured=true;break;}Ensure(await ReportNavigationCurrent(self,navigator,target,navigatorUnit,targetUnit,c.World.Enemies.Select(e=>NavToward(e,-10f,e.PositionY,-1.5f,1.2f)).ToList()),"online injury navigation rejected");await self.Root().GetComponent<TimerComponent>().WaitAsync(30);}Ensure(onlineInjured,"timed out waiting for online injured precondition");using G2C_GetCurrentWorld injured=await navigator.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldPlayerInfo injuredPlayer=injured.World.Players.First(x=>x.UnitId==targetUnit);int injuredHp=injuredPlayer.CurrentHP;using G2C_ReportWorldTransform retreatTarget=await target.Call(ReportWorldTransform(-10f,1f,-4f,0,injuredPlayer.LifecycleRevision)) as G2C_ReportWorldTransform;for(int cooldownStep=0;cooldownStep<10;cooldownStep++){Ensure(await ReportNavigationCurrent(self,navigator,target,navigatorUnit,targetUnit,Array.Empty<WorldEnemyNavigationReport>()),"pre-disconnect cooldown heartbeat rejected");await self.Root().GetComponent<TimerComponent>().WaitAsync(100);}
                target.Dispose();target=null;await WaitCurrent(self,navigator,x=>x.Players.Any(y=>y.UnitId==targetUnit&&y.ConnectionState==2&&!y.IsDead&&y.CurrentHP>0&&y.CurrentHP<=injuredHp),1500,"injured player entered grace");bool graceDamageObserved=false;long damageDeadline=TimeInfo.Instance.ClientNow()+900;while(TimeInfo.Instance.ClientNow()<damageDeadline){using G2C_GetCurrentWorld c=await navigator.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldPlayerInfo y=c.World.Players.FirstOrDefault(z=>z.UnitId==targetUnit);if(c.World.Corpses.Any(z=>z.SourceUnitId==targetUnit)){graceDamageObserved=true;break;}if(y==null||y.IsDead)break;if(y.CurrentHP<injuredHp)graceDamageObserved=true;Ensure(await ReportNavigationCurrent(self,navigator,navigator,navigatorUnit,navigatorUnit,c.World.Enemies.Select(e=>NavToward(e,-10f,e.PositionY,-4f,1.2f)).ToList()),"grace damage navigation rejected");await self.Root().GetComponent<TimerComponent>().WaitAsync(30);}Ensure(graceDamageObserved,"disconnected grace player took no further damage");await WaitCurrent(self,navigator,x=>x.Corpses.Any(y=>y.SourceUnitId==targetUnit),300,"grace player death");
                long expectedUnit=0;await WaitCurrent(self,navigator,x=>{WorldPlayerInfo y=x.Players.FirstOrDefault(z=>z.PlayerId==targetPlayerId&&z.UnitId!=targetUnit);if(y==null||y.ConnectionState!=2||y.IsDead||y.CurrentHP!=y.MaxHP)return false;expectedUnit=y.UnitId;return true;},1000,"grace player respawn with new identity");using Session reconnect=self.CreateSession();using G2C_Login login=await reconnect.Call(Login(targetUser)) as G2C_Login;using G2C_GetCurrentWorld current=await reconnect.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldPlayerInfo p=current.World.Players.First(x=>x.UnitId==expectedUnit);Ensure(login.Error==0&&login.IsPlayerActorReused&&current.HasWorld&&current.World.SelfUnitId==expectedUnit&&expectedUnit!=targetUnit,"grace reconnect did not reuse new combat unit");Ensure(p.ConnectionState==1&&!p.IsDead&&p.CurrentHP==p.MaxHP,"reconnect did not restore the full-health respawned unit");
            });
            failed+=await self.Case("WORLD-14 NavigationValidationAndSequence",async()=>
            {
                using Session primary=self.CreateSession();using Session secondary=self.CreateSession();using G2C_Login primaryLogin=await self.RegisterAndLogin(primary,UniqueUser("w14a"));using G2C_Login secondaryLogin=await self.RegisterAndLogin(secondary,UniqueUser("w14b"));using G2C_EnterWorld primaryEnter=await primary.Call(EnterWorld(Op(3086))) as G2C_EnterWorld;using G2C_EnterWorld secondaryEnter=await secondary.Call(EnterWorld(Op(3087))) as G2C_EnterWorld;long primaryUnit=primaryEnter.World.SelfUnitId,secondaryUnit=secondaryEnter.World.SelfUnitId;
                await WaitCurrent(self,primary,x=>(x.NavigationExecutorUnitId==primaryUnit||x.NavigationExecutorUnitId==secondaryUnit)&&x.Players.Any(y=>y.UnitId==primaryUnit&&y.ConnectionState==1)&&x.Players.Any(y=>y.UnitId==secondaryUnit&&y.ConnectionState==1)&&x.Enemies.Count>=2&&x.Enemies.All(y=>!y.IsDead),1500,"independent navigation validation precondition");using G2C_GetCurrentWorld c=await primary.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Session executor=c.World.NavigationExecutorUnitId==primaryUnit?primary:secondary;long lease=c.World.NavigationLeaseRevision;WorldEnemyInfo a=c.World.Enemies[0],b=c.World.Enemies[1];using G2C_ReportWorldEnemyNavigation empty=await executor.Call(Navigation(lease,1,Array.Empty<WorldEnemyNavigationReport>())) as G2C_ReportWorldEnemyNavigation;Ensure(empty.Accepted,"empty heartbeat rejected");using G2C_ReportWorldEnemyNavigation same=await executor.Call(Navigation(lease,2,new[]{Nav(a,a.PositionX,a.PositionY,a.PositionZ,a.LifecycleRevision)})) as G2C_ReportWorldEnemyNavigation;Ensure(same.Accepted&&same.WorldRevision==empty.WorldRevision,"same pose changed World revision");var mixed=new List<WorldEnemyNavigationReport>{Nav(a,a.PositionX+.1f,a.PositionY,a.PositionZ,a.LifecycleRevision),Nav(b,float.NaN,b.PositionY,b.PositionZ,b.LifecycleRevision)};using G2C_ReportWorldEnemyNavigation ok=await executor.Call(Navigation(lease,3,mixed)) as G2C_ReportWorldEnemyNavigation;Ensure(ok.Accepted,"mixed navigation batch rejected");using G2C_GetCurrentWorld after=await primary.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldEnemyInfo movedA=after.World.Enemies.First(x=>x.UnitId==a.UnitId),sameB=after.World.Enemies.First(x=>x.UnitId==b.UnitId);Ensure(movedA.PositionX!=a.PositionX&&sameB.PositionX==b.PositionX,"finite entry isolation failed");var ranged=new[]{Nav(movedA,movedA.PositionX+.1f,movedA.PositionY,movedA.PositionZ,movedA.LifecycleRevision),Nav(sameB,sameB.PositionX+100f,sameB.PositionY,sameB.PositionZ,sameB.LifecycleRevision)};using G2C_ReportWorldEnemyNavigation range=await executor.Call(Navigation(lease,4,ranged)) as G2C_ReportWorldEnemyNavigation;Ensure(range.Accepted,"range-isolation batch rejected");using G2C_GetCurrentWorld rangedAfter=await primary.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(rangedAfter.World.Enemies.First(x=>x.UnitId==a.UnitId).PositionX!=movedA.PositionX&&rangedAfter.World.Enemies.First(x=>x.UnitId==b.UnitId).PositionX==sameB.PositionX,"range entry isolation failed");using G2C_ReportWorldEnemyNavigation duplicate=await executor.Call(Navigation(lease,4,Array.Empty<WorldEnemyNavigationReport>())) as G2C_ReportWorldEnemyNavigation;Ensure(!duplicate.Accepted,"duplicate navigation sequence accepted");
            });
            firstEnter?.Dispose();secondEnter?.Dispose();thirdEnter?.Dispose();first?.Dispose();second?.Dispose();third?.Dispose();return failed;
        }

        private static async ETTask WaitCurrent(OnlineTestComponent self,Session session,Func<OnlineWorldSnapshot,bool> condition,int timeout,string name){long end=TimeInfo.Instance.ClientNow()+timeout;while(TimeInfo.Instance.ClientNow()<end){using G2C_GetCurrentWorld c=await session.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;if(c.HasWorld&&condition(c.World))return;await self.Root().GetComponent<TimerComponent>().WaitAsync(50);}throw new Exception($"timed out waiting for {name}");}
        private static C2G_WorldMeleeAttack Melee(long sequence,int weapon,float x,float z,long lifecycle){C2G_WorldMeleeAttack q=C2G_WorldMeleeAttack.Create();q.AttackSequence=sequence;q.WeaponItemId=weapon;q.AimX=x;q.AimZ=z;q.LifecycleRevision=lifecycle;return q;}
        private static WorldEnemyNavigationReport Nav(WorldEnemyInfo e,float x,float y,float z,long lifecycle){WorldEnemyNavigationReport r=WorldEnemyNavigationReport.Create();r.UnitId=e.UnitId;r.LifecycleRevision=lifecycle;r.PositionX=x;r.PositionY=y;r.PositionZ=z;r.YawDegrees=e.YawDegrees;return r;}
        private static WorldEnemyNavigationReport NavToward(WorldEnemyInfo e,float x,float y,float z,float maxStep){float dx=x-e.PositionX,dz=z-e.PositionZ,length=(float)Math.Sqrt(dx*dx+dz*dz);if(length>maxStep){dx=dx/length*maxStep;dz=dz/length*maxStep;}return Nav(e,e.PositionX+dx,y,e.PositionZ+dz,e.LifecycleRevision);}
        private static Session ExecutorSession(OnlineWorldSnapshot snapshot,Session first,long firstUnit,Session second,long secondUnit){if(snapshot.NavigationExecutorUnitId==firstUnit)return first;if(snapshot.NavigationExecutorUnitId==secondUnit)return second;throw new Exception($"unknown navigation executor {snapshot.NavigationExecutorUnitId}");}
        private static async ETTask<bool> ReportNavigationCurrent(OnlineTestComponent self,Session query,Session second,long firstUnit,long secondUnit,IReadOnlyList<WorldEnemyNavigationReport> reports){for(int attempt=0;attempt<10;attempt++){using G2C_GetCurrentWorld current=await query.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;if(!current.HasWorld)return false;Session executor=ExecutorSession(current.World,query,firstUnit,second,secondUnit);using G2C_ReportWorldEnemyNavigation response=await executor.Call(Navigation(current.World.NavigationLeaseRevision,TimeInfo.Instance.ClientNow()+attempt,reports)) as G2C_ReportWorldEnemyNavigation;if(response.Accepted)return true;await self.Root().GetComponent<TimerComponent>().WaitAsync(10);}return false;}
        private static C2G_ReportWorldEnemyNavigation Navigation(long lease,long sequence,IReadOnlyList<WorldEnemyNavigationReport> reports){C2G_ReportWorldEnemyNavigation q=C2G_ReportWorldEnemyNavigation.Create();q.NavigationLeaseRevision=lease;q.ReportSequence=sequence;foreach(WorldEnemyNavigationReport r in reports)q.Reports.Add(r);return q;}

        private static async ETTask<int> RunWorldRetry(this OnlineTestComponent self)
        {
            return await self.Case("WORLD-07 WorldLeaveFailureKeepsOffliningUntilRetry", async () =>
            {
                using Session observer = self.CreateSession(); using G2C_Login _ = await self.RegisterAndLogin(observer, UniqueUser("w07o")); using G2C_EnterWorld __ = await observer.Call(EnterWorld(Op(3060))) as G2C_EnterWorld;
                using Session target = self.CreateSession(); using G2C_Login ___ = await self.RegisterAndLogin(target, UniqueUser("w07t")); using G2C_EnterWorld targetEnter = await target.Call(EnterWorld(Op(3061))) as G2C_EnterWorld;
                int before = self.WorldLeftSessionIds.Count; using G2C_Logout logout = await target.Call(C2G_Logout.Create()) as G2C_Logout;
                await self.WaitForWorldPush(() => self.WorldLeftSessionIds.Count > before, 8000, "retry left push");
                Ensure(logout.Error == 0 && self.WorldLeftUnitIds.Last() == targetEnter.World.SelfUnitId, "retry did not eventually remove unit");
                using G2C_GetCurrentWorld current = await observer.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(current.World.Players.All(x => x.UnitId != targetEnter.World.SelfUnitId), "ghost unit remains after retry");
            });
        }

        private static async ETTask<int> RunWorldInventory(this OnlineTestComponent self)
        {
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-expiry",StringComparison.Ordinal))return await self.RunWorldInventoryExpiry();
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-save-failure",StringComparison.Ordinal))return await self.RunWorldInventorySaveFailure();
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-commit-loss",StringComparison.Ordinal))return await self.RunWorldInventoryCommitLoss();
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-migration-prepare",StringComparison.Ordinal))return await self.RunWorldInventoryMigrationPrepare();
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-migration",StringComparison.Ordinal)||string.Equals(Options.Instance.OnlineTestUser,"world-inventory-migration-restart",StringComparison.Ordinal))return await self.RunWorldInventoryMigration();
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-leave-prepare",StringComparison.Ordinal))return await self.RunWorldInventoryNamedPrepare("inventoryleave");
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-leave-failure",StringComparison.Ordinal))return await self.RunWorldInventoryLeaveFailure();
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-grace-prepare",StringComparison.Ordinal))return await self.RunWorldInventoryNamedPrepare("inventorygrace");
            if(string.Equals(Options.Instance.OnlineTestUser,"world-inventory-grace-save-failure",StringComparison.Ordinal))return await self.RunWorldInventoryGraceSaveFailure();
            int failed=0;
            failed += await self.RunHeldItemContracts();
            failed+=await self.Case("WORLD-INVENTORY-01 StarterIsGrantedOnceAndOperationsAreIdempotent",async()=>
            {
                Ensure(Options.Instance.OnlineWorldCorpseLifetimeMillisecondsOverride==0&&Options.Instance.OnlineProfileFailSaveCount==0&&Options.Instance.OnlineWorldDropLeaveCommitResponseCount==0,"inventory failure seams are not disabled by default");
                using Session session=self.CreateSession();using G2C_Login login=await self.RegisterAndLogin(session,UniqueUser("wi01"));
                using G2C_EnterWorld entered=await session.Call(EnterWorld(Op(4001))) as G2C_EnterWorld;
                Ensure(entered.Error==0&&entered.World.SelfInventory.Capacity==12&&entered.World.SelfInventory.Equipment.MeleeWeaponItemId==5,"first online starter sword was not equipped");
                long unit=entered.World.SelfUnitId;long lifecycle=entered.World.Players.First(x=>x.UnitId==unit).LifecycleRevision;long revision=entered.World.SelfInventory.Revision;
                using G2C_EnterWorld duplicate=await session.Call(EnterWorld(Op(4002))) as G2C_EnterWorld;
                Ensure(duplicate.Error==0&&duplicate.World.SelfUnitId==unit&&duplicate.World.SelfInventory.Revision==revision,"duplicate enter changed starter inventory");
                string operation=Op(4003);using G2C_OperateWorldInventory unequip=await session.Call(InventoryOperation(operation,revision,0,0,3,3,-1,1,0,lifecycle)) as G2C_OperateWorldInventory;
                Ensure(unequip.Error==0&&unequip.Accepted&&unequip.SelfInventory.Equipment.MeleeWeaponItemId==0&&unequip.SelfInventory.Slots.Any(x=>x.ItemId==5),"unequip failed");
                using G2C_OperateWorldInventory repeated=await session.Call(InventoryOperation(operation,revision,0,0,3,3,-1,1,0,lifecycle)) as G2C_OperateWorldInventory;
                Ensure(repeated.Error==0&&repeated.Accepted&&repeated.SelfInventory.Revision==unequip.SelfInventory.Revision,"same operation id was not idempotent");
                using G2C_OperateWorldInventory conflict=await session.Call(InventoryOperation(operation,unequip.SelfInventory.Revision,0,0,2,1,0,3,-1,lifecycle)) as G2C_OperateWorldInventory;
                Ensure(conflict.Error==ErrorCode.ERR_OnlineWorldOperationConflict,"different request reused operation id");
                using G2C_WorldMeleeAttack forged=await session.Call(Melee(1,5,0,1,lifecycle)) as G2C_WorldMeleeAttack;Ensure(!forged.Accepted,"unequipped sword attack accepted");
                using G2C_OperateWorldInventory equip=await session.Call(InventoryOperation(Op(4004),unequip.SelfInventory.Revision,0,0,2,1,0,3,-1,lifecycle)) as G2C_OperateWorldInventory;
                Ensure(equip.Error==0&&equip.Accepted&&equip.SelfInventory.Equipment.MeleeWeaponItemId==5,"equip failed");
                using G2C_WorldMeleeAttack legal=await session.Call(Melee(2,5,0,1,lifecycle)) as G2C_WorldMeleeAttack;Ensure(legal.Accepted,"equipped sword attack rejected");
            });
            failed+=await self.Case("WORLD-INVENTORY-02 EnemyCorpseSharedAtomicAfterEnemyRemoval",async()=>
            {
                using Session first=self.CreateSession();using G2C_Login _=await self.RegisterAndLogin(first,UniqueUser("wi02a"));using G2C_EnterWorld entered=await first.Call(EnterWorld(Op(4010))) as G2C_EnterWorld;
                using Session second=self.CreateSession();using G2C_Login __=await self.RegisterAndLogin(second,UniqueUser("wi02b"));using G2C_EnterWorld joined=await second.Call(EnterWorld(Op(4011))) as G2C_EnterWorld;
                WorldPlayerInfo player=entered.World.Players.First(x=>x.UnitId==entered.World.SelfUnitId);WorldEnemyInfo enemy=entered.World.Enemies[0];
                using G2C_ReportWorldTransform near=await first.Call(ReportWorldTransform(enemy.PositionX,enemy.PositionY,enemy.PositionZ-1,0,player.LifecycleRevision)) as G2C_ReportWorldTransform;
                using G2C_WorldMeleeAttack hit1=await first.Call(Melee(1,5,0,1,player.LifecycleRevision)) as G2C_WorldMeleeAttack;await self.Root().GetComponent<TimerComponent>().WaitAsync(550);using G2C_WorldMeleeAttack hit2=await first.Call(Melee(2,5,0,1,player.LifecycleRevision)) as G2C_WorldMeleeAttack;
                Ensure(hit1.Accepted&&hit2.Accepted,"enemy kill attacks failed");using G2C_GetCurrentWorld current=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;WorldCorpseInfo corpse=current.World.Corpses.Single(x=>x.SourceUnitId==enemy.UnitId);Ensure(corpse.HasContents,"enemy corpse missing contents");
                WorldPlayerInfo secondPlayer=joined.World.Players.First(x=>x.UnitId==joined.World.SelfUnitId);using G2C_OpenWorldCorpse farOpen=await second.Call(OpenCorpse(corpse.CorpseId,secondPlayer.LifecycleRevision)) as G2C_OpenWorldCorpse;Ensure(farOpen.Error==ErrorCode.ERR_OnlineWorldSearchInvalid,"far-distance corpse search succeeded");using G2C_OperateWorldInventory secondUnequip=await second.Call(InventoryOperation(Op(4014),joined.World.SelfInventory.Revision,0,0,3,3,-1,1,0,secondPlayer.LifecycleRevision)) as G2C_OperateWorldInventory;Ensure(secondUnequip.Accepted,"capacity setup unequip failed");
                using G2C_ReportWorldTransform secondNear=await second.Call(ReportWorldTransform(corpse.PositionX,corpse.PositionY,corpse.PositionZ,0,joined.World.Players.First(x=>x.UnitId==joined.World.SelfUnitId).LifecycleRevision)) as G2C_ReportWorldTransform;
                using G2C_OpenWorldCorpse open1=await first.Call(OpenCorpse(corpse.CorpseId,player.LifecycleRevision)) as G2C_OpenWorldCorpse;using G2C_OpenWorldCorpse open2=await second.Call(OpenCorpse(corpse.CorpseId,joined.World.Players.First(x=>x.UnitId==joined.World.SelfUnitId).LifecycleRevision)) as G2C_OpenWorldCorpse;
                Ensure(open1.Error==0&&open2.Error==0&&open1.CorpseContents.Slots.Count>0,"shared corpse could not be opened");int slot=open1.CorpseContents.Slots[0].SlotIndex;
                await self.Root().GetComponent<TimerComponent>().WaitAsync(900);using G2C_GetCurrentWorld afterEnemyRemoval=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(afterEnemyRemoval.World.Enemies.All(x=>x.UnitId!=enemy.UnitId)&&afterEnemyRemoval.World.Corpses.Any(x=>x.CorpseId==corpse.CorpseId),"removed enemy returned or its independent non-empty corpse was lost");
                using G2C_OperateWorldInventory staleInventory=await first.Call(InventoryOperation(Op(4015),open1.SelfInventory.Revision-1,corpse.CorpseId,corpse.StateRevision,1,2,slot,1,11,player.LifecycleRevision)) as G2C_OperateWorldInventory;Ensure(staleInventory.Error==ErrorCode.ERR_OnlineWorldInventoryRevisionConflict&&!staleInventory.Accepted,"stale inventory revision modified data");using G2C_OperateWorldInventory capacity=await second.Call(InventoryOperation(Op(4016),open2.SelfInventory.Revision,corpse.CorpseId,corpse.StateRevision,1,2,slot,1,0,secondPlayer.LifecycleRevision)) as G2C_OperateWorldInventory;Ensure(capacity.Error==ErrorCode.ERR_OnlineWorldInventoryCapacity&&!capacity.Accepted,"insufficient bag capacity modified data");
                long takeInventoryRevision=open1.SelfInventory.Revision;long takeCorpseRevision=corpse.StateRevision;bool allTaken=true;int targetSlot=open1.SelfInventory.Slots.Max(x=>x.SlotIndex)+1;foreach(OnlineItemSlotInfo corpseSlot in open1.CorpseContents.Slots.ToArray()){using G2C_OperateWorldInventory take=await first.Call(InventoryOperation(Op(40120+targetSlot),takeInventoryRevision,corpse.CorpseId,takeCorpseRevision,1,2,corpseSlot.SlotIndex,1,targetSlot,player.LifecycleRevision)) as G2C_OperateWorldInventory;allTaken&=take.Accepted;takeInventoryRevision=take.SelfInventory.Revision;if(take.HasCorpse)takeCorpseRevision=take.CorpseContents.Corpse.StateRevision;targetSlot++;}
                using G2C_OperateWorldInventory take2=await second.Call(InventoryOperation(Op(4013),open2.SelfInventory.Revision,corpse.CorpseId,corpse.StateRevision,1,2,slot,1,1,joined.World.Players.First(x=>x.UnitId==joined.World.SelfUnitId).LifecycleRevision)) as G2C_OperateWorldInventory;
                Ensure(allTaken&&!take2.Accepted,"concurrent corpse claim was not atomic");using G2C_GetCurrentWorld emptied=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(emptied.World.Corpses.All(x=>x.CorpseId!=corpse.CorpseId),"empty corpse was not removed immediately");
                await self.Root().GetComponent<TimerComponent>().WaitAsync(100);using G2C_GetCurrentWorld afterRemoval=await first.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(afterRemoval.World.Corpses.All(x=>x.CorpseId!=corpse.CorpseId),"removed empty corpse reappeared");
            });
            failed+=await self.Case("WORLD-INVENTORY-03 CrossMapIsolationAndLiveLeavePersistence",async()=>
            {
                using Session session=self.CreateSession();using G2C_Login _=await self.RegisterAndLogin(session,UniqueUser("wi03"));using G2C_EnterWorld first=await session.Call(EnterWorld(Op(4020),MainWorldMapId)) as G2C_EnterWorld;long revision=first.World.SelfInventory.Revision;
                using G2C_LeaveWorld leave=await session.Call(LeaveWorld(Op(4021))) as G2C_LeaveWorld;using G2C_EnterWorld second=await session.Call(EnterWorld(Op(4022),VillageWorldMapId)) as G2C_EnterWorld;
                Ensure(leave.Error==0&&second.Error==0&&second.World.Corpses.Count==0&&second.World.SelfInventory.Revision==revision&&second.World.SelfInventory.Equipment.MeleeWeaponItemId==5,"live leave persistence or cross-map isolation failed");
            });
            failed+=await self.Case("WORLD-INVENTORY-07 ProfileSaveOperationIdIsIdempotentAndConflictingCandidateIsRejected",async()=>
            {
                using Session session=self.CreateSession();using G2C_Login login=await self.RegisterAndLogin(session,UniqueUser("wi07"));using G2C_EnterWorld entered=await session.Call(EnterWorld(Op(4070))) as G2C_EnterWorld;
                using C2G_ExecuteServerTestCmd command=C2G_ExecuteServerTestCmd.Create();command.CommandText="world-profile-save-idempotency";using G2C_ExecuteServerTestCmd result=await session.Call(command) as G2C_ExecuteServerTestCmd;Ensure(result.Error==0&&result.Recognized&&result.Success,$"profile save idempotency command failed: {result.Message}");
            });
            return failed;
        }

        private static async ETTask<int> RunWorldInventorySaveFailure(this OnlineTestComponent self)
        {
            return await self.Case("WORLD-INVENTORY-04 ProfileSaveFailureRetriesStarterExactlyOnce",async()=>
            {
                using Session session=self.CreateSession();using G2C_Login _=await self.RegisterAndLogin(session,UniqueUser("wi04"));
                using G2C_EnterWorld failed=await session.Call(EnterWorld(Op(4030))) as G2C_EnterWorld;Ensure(failed.Error==ErrorCode.ERR_OnlineProfileSaveFailed&&!failed.HasWorld,"injected starter save did not fail atomically");
                using G2C_EnterWorld retried=await session.Call(EnterWorld(Op(4031))) as G2C_EnterWorld;Ensure(retried.Error==0&&retried.World.SelfInventory.Equipment.MeleeWeaponItemId==5&&retried.World.SelfInventory.Slots.Count(x=>x.ItemId==5)==1,"starter retry duplicated or lost the sword");
            });
        }

        private static async ETTask<int> RunWorldInventoryMigrationPrepare(this OnlineTestComponent self)
        {
            return await self.Case("WORLD-INVENTORY-MIGRATION-PREPARE",async()=>{using Session session=self.CreateSession();using G2C_Login login=await self.RegisterAndLogin(session,"inventorylegacy");Ensure(login.Error==0,"legacy migration seed account was not created");});
        }

        private static async ETTask<int> RunWorldInventoryMigration(this OnlineTestComponent self)
        {
            return await self.Case("WORLD-INVENTORY-08 VersionOneProfileMigratesToClaimedEmptyAndNeverRegrants",async()=>
            {
                using Session session=self.CreateSession();using G2C_Login login=await session.Call(Login("inventorylegacy")) as G2C_Login;Ensure(login.Error==0,"migrated account login failed");using G2C_EnterWorld entered=await session.Call(EnterWorld(Op(4080))) as G2C_EnterWorld;Ensure(entered.Error==0&&entered.World.SelfInventory.Slots.Count==0&&entered.World.SelfInventory.Equipment.MeleeWeaponItemId==0,"version one profile received starter supplies after migration");using G2C_LeaveWorld left=await session.Call(LeaveWorld(Op(4081))) as G2C_LeaveWorld;Ensure(left.Error==0,"migrated profile leave save failed");
            });
        }

        private static async ETTask<int> RunWorldInventoryNamedPrepare(this OnlineTestComponent self,string user)
        {
            return await self.Case("WORLD-INVENTORY-PERSISTED-PREPARE",async()=>{using Session session=self.CreateSession();using G2C_Login login=await self.RegisterAndLogin(session,user);using G2C_EnterWorld entered=await session.Call(EnterWorld(Op(4090))) as G2C_EnterWorld;using G2C_LeaveWorld left=await session.Call(LeaveWorld(Op(4091))) as G2C_LeaveWorld;Ensure(login.Error==0&&entered.Error==0&&left.Error==0,"persisted test profile preparation failed");});
        }

        private static async ETTask<int> RunWorldInventoryLeaveFailure(this OnlineTestComponent self)
        {
            return await self.Case("WORLD-INVENTORY-09 FailedLeaveAbortsBeforeUnfreezeAndSecondLeaveCommitsOnce",async()=>
            {
                using Session session=self.CreateSession();using G2C_Login login=await session.Call(Login("inventoryleave")) as G2C_Login;using G2C_EnterWorld entered=await session.Call(EnterWorld(Op(4092))) as G2C_EnterWorld;long unit=entered.World.SelfUnitId;WorldPlayerInfo player=entered.World.Players.First(x=>x.UnitId==unit);using G2C_LeaveWorld failed=await session.Call(LeaveWorld(Op(4093))) as G2C_LeaveWorld;Ensure(failed.Error==ErrorCode.ERR_OnlineProfileSaveFailed,"injected active leave save did not fail");using G2C_GetCurrentWorld current=await session.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(current.HasWorld&&current.World.SelfUnitId==unit,"failed leave removed or hid the character");using G2C_ReportWorldTransform moved=await session.Call(ReportWorldTransform(3,.1f,3,0,player.LifecycleRevision)) as G2C_ReportWorldTransform;Ensure(moved.Error==0,"confirmed abort did not restore movement");using G2C_LeaveWorld retried=await session.Call(LeaveWorld(Op(4094))) as G2C_LeaveWorld;using G2C_GetCurrentWorld gone=await session.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(retried.Error==0&&!gone.HasWorld,"second non-competing leave did not remove exactly once");
            });
        }

        private static async ETTask<int> RunWorldInventoryGraceSaveFailure(this OnlineTestComponent self)
        {
            return await self.Case("WORLD-INVENTORY-10 GraceTimeoutRejectsReloginUntilEmptyProfileSaveCloses",async()=>
            {
                Session first=self.CreateSession();using G2C_Login login=await first.Call(Login("inventorygrace")) as G2C_Login;using G2C_EnterWorld entered=await first.Call(EnterWorld(Op(4100))) as G2C_EnterWorld;Ensure(login.Error==0&&entered.Error==0,"grace failure setup login failed");first.Dispose();await self.Root().GetComponent<TimerComponent>().WaitAsync(1500);using Session blocked=self.CreateSession();using G2C_Login rejected=await blocked.Call(Login("inventorygrace")) as G2C_Login;Ensure(rejected.Error!=0,"relogin succeeded while old actor still owned the failed empty-profile save");await self.Root().GetComponent<TimerComponent>().WaitAsync(5500);using Session recovered=self.CreateSession();using G2C_Login retry=await recovered.Call(Login("inventorygrace")) as G2C_Login;Ensure(retry.Error==0&&!retry.IsPlayerActorReused,"profile retry did not release the old actor");using G2C_EnterWorld reentered=await recovered.Call(EnterWorld(Op(4101))) as G2C_EnterWorld;Ensure(reentered.Error==0&&reentered.World.SelfInventory.Slots.Count==0&&reentered.World.SelfInventory.Equipment.MeleeWeaponItemId==0,"timeout recovery did not recreate an empty character");
            });
        }

        private static async ETTask<int> RunWorldInventoryCommitLoss(this OnlineTestComponent self)
        {
            return await self.Case("WORLD-INVENTORY-05 CompletedLeaveResponseLossIsIdempotent",async()=>
            {
                using Session session=self.CreateSession();using G2C_Login _=await self.RegisterAndLogin(session,UniqueUser("wi05"));using G2C_EnterWorld entered=await session.Call(EnterWorld(Op(4040))) as G2C_EnterWorld;
                using G2C_LeaveWorld left=await session.Call(LeaveWorld(Op(4041))) as G2C_LeaveWorld;Ensure(left.Error==0,"same operation commit retry did not close the leave transaction");using G2C_EnterWorld reentered=await session.Call(EnterWorld(Op(4042))) as G2C_EnterWorld;Ensure(reentered.Error==0&&reentered.World.SelfUnitId!=entered.World.SelfUnitId&&reentered.World.SelfInventory.Revision==entered.World.SelfInventory.Revision,"commit retry removed twice or re-saved inventory");
            });
        }

        private static async ETTask<int> RunWorldInventoryExpiry(this OnlineTestComponent self)
        {
            return await self.Case("WORLD-INVENTORY-06 CorpseExpiresWithTestOverride",async()=>
            {
                using Session session=self.CreateSession();using G2C_Login _=await self.RegisterAndLogin(session,UniqueUser("wi06"));using G2C_EnterWorld entered=await session.Call(EnterWorld(Op(4050))) as G2C_EnterWorld;WorldPlayerInfo player=entered.World.Players.First(x=>x.UnitId==entered.World.SelfUnitId);WorldEnemyInfo enemy=entered.World.Enemies[0];
                using G2C_ReportWorldTransform near=await session.Call(ReportWorldTransform(enemy.PositionX,enemy.PositionY,enemy.PositionZ-1,0,player.LifecycleRevision)) as G2C_ReportWorldTransform;using G2C_WorldMeleeAttack first=await session.Call(Melee(1,5,0,1,player.LifecycleRevision)) as G2C_WorldMeleeAttack;await self.Root().GetComponent<TimerComponent>().WaitAsync(550);using G2C_WorldMeleeAttack second=await session.Call(Melee(2,5,0,1,player.LifecycleRevision)) as G2C_WorldMeleeAttack;Ensure(first.Accepted&&second.Accepted,"expiry setup kill failed");await self.Root().GetComponent<TimerComponent>().WaitAsync(400);using G2C_GetCurrentWorld current=await session.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;Ensure(current.World.Corpses.All(x=>x.SourceUnitId!=enemy.UnitId),"expired corpse remained visible");
            });
        }

        private static async ETTask<int> RunWorldMaps(this OnlineTestComponent self)
        {
            int failed = 0;
            failed += await self.Case("WORLD-MAPS-01 SharedAndIsolatedSnapshots", async () =>
            {
                using Session survivalA = self.CreateSession(); using G2C_Login survivalALogin = await self.RegisterAndLogin(survivalA, UniqueUser("wm01a"));
                using G2C_EnterWorld survivalAEnter = await survivalA.Call(EnterWorld(Op(3201), MainWorldMapId)) as G2C_EnterWorld;
                using Session survivalB = self.CreateSession(); using G2C_Login survivalBLogin = await self.RegisterAndLogin(survivalB, UniqueUser("wm01b"));
                using G2C_EnterWorld survivalBEnter = await survivalB.Call(EnterWorld(Op(3202), MainWorldMapId)) as G2C_EnterWorld;
                using Session mission = self.CreateSession(); using G2C_Login missionLogin = await self.RegisterAndLogin(mission, UniqueUser("wm01m"));
                using G2C_EnterWorld missionEnter = await mission.Call(EnterWorld(Op(3203), MissionWorldMapId)) as G2C_EnterWorld;
                using Session village = self.CreateSession(); using G2C_Login villageLogin = await self.RegisterAndLogin(village, UniqueUser("wm01v"));
                using G2C_EnterWorld villageEnter = await village.Call(EnterWorld(Op(3204), VillageWorldMapId)) as G2C_EnterWorld;

                Ensure(new[] { survivalAEnter, survivalBEnter, missionEnter, villageEnter }.All(x => x.Error == 0 && x.World.WorldId == MainWorldId), "maps did not share WorldId=1");
                Ensure(survivalBEnter.World.Players.Any(x => x.PlayerId == survivalALogin.PlayerId) && survivalBEnter.World.Players.Any(x => x.PlayerId == survivalBLogin.PlayerId), "same-map players were not shared");
                Ensure(missionEnter.World.MapId == MissionWorldMapId && missionEnter.World.Players.Count == 1 && missionEnter.World.Players[0].PlayerId == missionLogin.PlayerId && missionEnter.World.Enemies.Count == 0 && missionEnter.World.NavigationExecutorUnitId == 0, "mission map was not isolated and empty-enemy");
                Ensure(villageEnter.World.MapId == VillageWorldMapId && villageEnter.World.Players.Count == 1 && villageEnter.World.Players[0].PlayerId == villageLogin.PlayerId && villageEnter.World.Enemies.Count == 0 && villageEnter.World.NavigationExecutorUnitId == 0, "village map was not isolated and empty-enemy");
                long[] units = { survivalAEnter.World.SelfUnitId, survivalBEnter.World.SelfUnitId, missionEnter.World.SelfUnitId, villageEnter.World.SelfUnitId };
                Ensure(units.Distinct().Count() == units.Length, "unit ids were not globally unique");

                long missionRevision = missionEnter.World.WorldRevision;
                int missionPushes = self.WorldTransformSessionIds.Count(x => x == mission.Id);
                WorldPlayerInfo actor = survivalAEnter.World.Players.First(x => x.UnitId == survivalAEnter.World.SelfUnitId);
                using G2C_ReportWorldTransform moved = await survivalA.Call(ReportWorldTransform(6f, .1f, -4f, 0f, actor.LifecycleRevision)) as G2C_ReportWorldTransform;
                await self.WaitForWorldPush(() => self.WorldTransformSessionIds.Count(x => x == survivalB.Id) > 0, 2000, "same-map transform push");
                using G2C_GetCurrentWorld missionCurrent = await mission.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(moved.Error == 0 && self.WorldTransformSessionIds.Count(x => x == mission.Id) == missionPushes, "cross-map transform was forwarded");
                Ensure(missionCurrent.World.WorldRevision == missionRevision, "survival revision advanced mission revision");
            });

            failed += await self.Case("WORLD-MAPS-02 ReentryConflictLeaveAndSwitch", async () =>
            {
                using Session session = self.CreateSession(); using G2C_Login _ = await self.RegisterAndLogin(session, UniqueUser("wm02"));
                using G2C_EnterWorld first = await session.Call(EnterWorld(Op(3210), MissionWorldMapId)) as G2C_EnterWorld;
                using G2C_EnterWorld same = await session.Call(EnterWorld(Op(3211), MissionWorldMapId)) as G2C_EnterWorld;
                using G2C_EnterWorld conflict = await session.Call(EnterWorld(Op(3212), VillageWorldMapId)) as G2C_EnterWorld;
                Ensure(first.Error == 0 && same.Error == 0 && same.World.SelfUnitId == first.World.SelfUnitId, "same-map enter did not reuse unit");
                Ensure(conflict.Error == ErrorCode.ERR_OnlineWorldStateConflict, "different-map enter did not conflict");
                using G2C_LeaveWorld left = await session.Call(LeaveWorld(Op(3213))) as G2C_LeaveWorld;
                using G2C_EnterWorld switched = await session.Call(EnterWorld(Op(3214), VillageWorldMapId)) as G2C_EnterWorld;
                Ensure(left.Error == 0 && switched.Error == 0 && switched.World.MapId == VillageWorldMapId && switched.World.SelfUnitId != first.World.SelfUnitId, "leave then switch failed");
            });

            failed += await self.Case("WORLD-MAPS-03 InvalidMapKeepsRoom", async () =>
            {
                using Session session = self.CreateSession(); using G2C_Login login = await self.RegisterAndLogin(session, UniqueUser("wm03"));
                using G2C_CreateRoom created = await session.Call(CreateRoom(Op(3220))) as G2C_CreateRoom;
                using G2C_EnterWorld invalid = await session.Call(EnterWorld(Op(3221), "map.invalid")) as G2C_EnterWorld;
                using G2C_GetCurrentRoom current = await session.Call(C2G_GetCurrentRoom.Create()) as G2C_GetCurrentRoom;
                Ensure(invalid.Error == ErrorCode.ERR_OnlineWorldOperationInvalid && current.HasRoom && current.Room.Members.Any(x => x.PlayerId == login.PlayerId), "invalid map changed room membership");
            });

            failed += await self.Case("WORLD-MAPS-04 GraceReconnectRestoresMapIdentity", async () =>
            {
                string user = UniqueUser("wm04");
                Session first = self.CreateSession(); using G2C_Login _ = await self.RegisterAndLogin(first, user); using G2C_EnterWorld entered = await first.Call(EnterWorld(Op(3230), VillageWorldMapId)) as G2C_EnterWorld;
                long unitId = entered.World.SelfUnitId; first.Dispose();
                await self.Root().GetComponent<TimerComponent>().WaitAsync(250);
                using Session reconnect = self.CreateSession(); using G2C_Login login = await reconnect.Call(Login(user)) as G2C_Login; using G2C_GetCurrentWorld current = await reconnect.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(login.Error == 0 && login.IsPlayerActorReused && current.Error == 0 && current.HasWorld && current.World.MapId == VillageWorldMapId && current.World.SelfUnitId == unitId, "grace reconnect lost map identity");
            });

            failed += await self.Case("WORLD-MAPS-05 EnterLeaveBroadcastsStayWithinMap", async () =>
            {
                using Session missionObserver = self.CreateSession();
                using G2C_Login missionLogin = await self.RegisterAndLogin(missionObserver, UniqueUser("wm05m"));
                using G2C_EnterWorld missionEnter = await missionObserver.Call(EnterWorld(Op(3240), MissionWorldMapId)) as G2C_EnterWorld;
                using Session villageObserver = self.CreateSession();
                using G2C_Login villageLogin = await self.RegisterAndLogin(villageObserver, UniqueUser("wm05v"));
                using G2C_EnterWorld villageEnter = await villageObserver.Call(EnterWorld(Op(3241), VillageWorldMapId)) as G2C_EnterWorld;
                using G2C_GetCurrentWorld villageBefore = await villageObserver.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;

                long villageRevision = villageBefore.World.WorldRevision;
                long[] villagePlayers = villageBefore.World.Players.Select(x => x.PlayerId).OrderBy(x => x).ToArray();
                int missionEnteredBefore = self.WorldEnteredSessionIds.Count(x => x == missionObserver.Id);
                int villageEnteredBefore = self.WorldEnteredSessionIds.Count(x => x == villageObserver.Id);
                using Session entrant = self.CreateSession();
                using G2C_Login entrantLogin = await self.RegisterAndLogin(entrant, UniqueUser("wm05e"));
                using G2C_EnterWorld entrantEnter = await entrant.Call(EnterWorld(Op(3242), MissionWorldMapId)) as G2C_EnterWorld;

                await self.WaitForWorldPush(
                    () => self.WorldEnteredSessionIds.Count(x => x == missionObserver.Id) > missionEnteredBefore,
                    2000,
                    "mission entered push");
                int enteredIndex = self.WorldEnteredSessionIds.FindLastIndex(x => x == missionObserver.Id);
                Ensure(entrantEnter.Error == 0 && self.WorldEnteredPlayers[enteredIndex].PlayerId == entrantLogin.PlayerId && self.WorldEnteredPlayers[enteredIndex].MapId == MissionWorldMapId,
                    "same-map observer did not receive the entrant identity");
                Ensure(self.WorldEnteredSessionIds.Count(x => x == villageObserver.Id) == villageEnteredBefore,
                    "other-map observer received entered push");
                using G2C_GetCurrentWorld missionAfterEnter = await missionObserver.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                using G2C_GetCurrentWorld villageAfterEnter = await villageObserver.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(missionAfterEnter.World.WorldRevision > missionEnter.World.WorldRevision && missionAfterEnter.World.Players.Any(x => x.PlayerId == entrantLogin.PlayerId),
                    "mission snapshot did not advance for same-map entry");
                Ensure(villageAfterEnter.World.WorldRevision == villageRevision && villageAfterEnter.World.Players.Select(x => x.PlayerId).OrderBy(x => x).SequenceEqual(villagePlayers),
                    "other-map snapshot changed after entry");

                int missionLeftBefore = self.WorldLeftSessionIds.Count(x => x == missionObserver.Id);
                int villageLeftBefore = self.WorldLeftSessionIds.Count(x => x == villageObserver.Id);
                using G2C_LeaveWorld entrantLeft = await entrant.Call(LeaveWorld(Op(3243))) as G2C_LeaveWorld;
                await self.WaitForWorldPush(
                    () => self.WorldLeftSessionIds.Count(x => x == missionObserver.Id) > missionLeftBefore,
                    2000,
                    "mission left push");
                int leftIndex = self.WorldLeftSessionIds.FindLastIndex(x => x == missionObserver.Id);
                Ensure(entrantLeft.Error == 0 && self.WorldLeftUnitIds[leftIndex] == entrantEnter.World.SelfUnitId,
                    "same-map observer did not receive the departing unit");
                Ensure(self.WorldLeftSessionIds.Count(x => x == villageObserver.Id) == villageLeftBefore,
                    "other-map observer received left push");
                using G2C_GetCurrentWorld missionAfterLeft = await missionObserver.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                using G2C_GetCurrentWorld villageAfterLeft = await villageObserver.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(missionAfterLeft.World.WorldRevision > missionAfterEnter.World.WorldRevision && missionAfterLeft.World.Players.All(x => x.PlayerId != entrantLogin.PlayerId),
                    "mission snapshot did not advance for same-map leave");
                Ensure(villageAfterLeft.World.WorldRevision == villageRevision && villageAfterLeft.World.Players.Select(x => x.PlayerId).OrderBy(x => x).SequenceEqual(villagePlayers),
                    "other-map snapshot changed after leave");
                Ensure(missionLogin.PlayerId != villageLogin.PlayerId && missionEnter.World.MapId != villageEnter.World.MapId,
                    "cross-map observer precondition failed");
            });

            failed += await self.Case("WORLD-MAPS-06 LateOldMapPushIsDroppedAfterSwitch", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await self.RegisterAndLogin(session, UniqueUser("wm06"));
                using G2C_EnterWorld oldEnter = await session.Call(EnterWorld(Op(3250), MissionWorldMapId)) as G2C_EnterWorld;
                long oldUnitId = oldEnter.World.SelfUnitId;
                using G2C_LeaveWorld left = await session.Call(LeaveWorld(Op(3251))) as G2C_LeaveWorld;
                using G2C_EnterWorld currentEnter = await session.Call(EnterWorld(Op(3252), VillageWorldMapId)) as G2C_EnterWorld;
                Ensure(oldEnter.Error == 0 && left.Error == 0 && currentEnter.Error == 0 && currentEnter.World.SelfUnitId != oldUnitId,
                    "old-to-new map switch precondition failed");

                int transformBefore = self.WorldTransformSessionIds.Count(x => x == session.Id);
                using C2G_ExecuteServerTestCmd command = C2G_ExecuteServerTestCmd.Create();
                command.CommandText = $"world-stale-transform {MissionWorldMapId} {oldUnitId}";
                using G2C_ExecuteServerTestCmd result = await session.Call(command) as G2C_ExecuteServerTestCmd;
                Ensure(result.Error == 0 && result.Recognized && result.Success && result.ResultCode == (int)ServerTestCmdResultCode.Success,
                    $"stale transform injection failed code={result.ResultCode} message={result.Message}");
                Ensure(string.Equals(result.Message, $"{VillageWorldMapId}|{currentEnter.World.SelfUnitId}", StringComparison.Ordinal),
                    $"player actor barrier returned unexpected identity: {result.Message}");

                await self.Root().GetComponent<TimerComponent>().WaitAsync(200);
                using G2C_GetCurrentWorld current = await session.Call(C2G_GetCurrentWorld.Create()) as G2C_GetCurrentWorld;
                Ensure(self.WorldTransformSessionIds.Count(x => x == session.Id) == transformBefore,
                    "late old-map transform was forwarded to the current Gate session");
                Ensure(current.Error == 0 && current.HasWorld && current.World.MapId == VillageWorldMapId && current.World.SelfUnitId == currentEnter.World.SelfUnitId && current.World.Players.Any(x => x.PlayerId == login.PlayerId),
                    "late old-map transform changed the current world identity");
            });
            return failed;
        }

        private static async ETTask WaitForWorldPush(this OnlineTestComponent self, Func<bool> condition, int timeoutMs, string name)
        {
            long end = TimeInfo.Instance.ClientNow() + timeoutMs;
            while (TimeInfo.Instance.ClientNow() < end) { if (condition()) return; await self.Root().GetComponent<TimerComponent>().WaitAsync(50); }
            throw new Exception($"timed out waiting for {name}");
        }

        private static C2G_EnterWorld EnterWorld(string operationId, string mapId = MainWorldMapId) { C2G_EnterWorld request = C2G_EnterWorld.Create(); request.InventorySchemaVersion=1;request.OperationId = operationId; request.MapId = mapId; return request; }
        private static C2G_LeaveWorld LeaveWorld(string operationId) { C2G_LeaveWorld request = C2G_LeaveWorld.Create(); request.OperationId = operationId; return request; }
        private static C2G_OpenWorldCorpse OpenCorpse(long corpseId,long lifecycle){C2G_OpenWorldCorpse request=C2G_OpenWorldCorpse.Create();request.CorpseId=corpseId;request.LifecycleRevision=lifecycle;return request;}
        private static C2G_OperateWorldInventory InventoryOperation(string operationId,long inventoryRevision,long corpseId,long corpseRevision,int action,int sourceKind,int sourceSlot,int targetKind,int targetSlot,long lifecycle){C2G_OperateWorldInventory request=C2G_OperateWorldInventory.Create();request.OperationId=operationId;request.ExpectedInventoryRevision=inventoryRevision;request.CorpseId=corpseId;request.ExpectedCorpseRevision=corpseRevision;request.InventorySchemaVersion=1;request.ExpectedItemId=action is 2 or 6?5:action is 4 or 7 or 8?6:0;request.ActionKind=action is 2 or 4 or 6 or 7?9:action is 3 or 5?10:action==8?11:action;request.SourceContainerKind=action is 6 or 7 or 8?1:sourceKind;request.SourceSlotIndex=action is 7 or 8?1:sourceSlot;request.TargetContainerKind=targetKind;request.TargetSlotIndex=targetSlot;request.LifecycleRevision=lifecycle;return request;}
        private static C2G_ReportWorldTransform ReportWorldTransform(float x, float y, float z, float yaw,long lifecycle) { C2G_ReportWorldTransform request = C2G_ReportWorldTransform.Create(); request.PositionX=x; request.PositionY=y; request.PositionZ=z; request.YawDegrees=yaw;request.LifecycleRevision=lifecycle; return request; }
    }

    [ServerTestCmdHandler("server.world-profile-save-idempotency", "world-profile-save-idempotency", "server.general", 0, "服务器", 1000,
        "资料保存幂等", "$world-profile-save-idempotency", "测试相同 OperationId 候选只提交一次且冲突候选被拒绝", "$world-profile-save-idempotency", "$world-profile-save-idempotency")]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class WorldProfileSaveIdempotencyTestCmdHandler:IServerTestCmdHandler
    {
        public async ETTask<ServerTestCmdRunResult> Run(Scene root,Session session,string argumentText)
        {
            GatePlayerProxy proxy=session.GetComponent<OnlineSessionComponent>()?.Proxy;if(proxy==null||proxy.PlayerActorId==default)return new ServerTestCmdRunResult(true,false,ServerTestCmdResultCode.ExecutionFailed,"当前 Gate 会话未认证");
            G2P_GetCurrentWorld query=G2P_GetCurrentWorld.Create();query.PlayerId=proxy.PlayerId;query.SessionEpoch=proxy.SessionEpoch;using P2G_GetCurrentWorld current=await root.GetComponent<MessageSender>().Call(proxy.PlayerActorId,query,false) as P2G_GetCurrentWorld;if(current?.World?.SelfInventory==null)return new ServerTestCmdRunResult(true,false,ServerTestCmdResultCode.ExecutionFailed,"当前联机物资不可用");
            string display=current.World.Players.First(x=>x.UnitId==current.World.SelfUnitId).DisplayName;string operation=$"profile-idempotency-{proxy.PlayerId}";ActorId account=root.GetOnlineSceneActorId("Account");
            P2A_SavePlayerProfile Make(string name){P2A_SavePlayerProfile value=P2A_SavePlayerProfile.Create();value.AccountId=proxy.AccountId;value.PlayerId=proxy.PlayerId;value.DisplayName=name;value.OperationId=operation;value.ExpectedRevision=2;value.OnlineInventory=OnlineWorldSnapshotHelper.Clone(current.World.SelfInventory);value.HasReceivedOnlineStarterSupplies=true;return value;}
            using A2P_SavePlayerProfile first=await root.GetComponent<MessageSender>().Call(account,Make(display),false) as A2P_SavePlayerProfile;using A2P_SavePlayerProfile repeat=await root.GetComponent<MessageSender>().Call(account,Make(display),false) as A2P_SavePlayerProfile;using A2P_SavePlayerProfile conflict=await root.GetComponent<MessageSender>().Call(account,Make(display+"-different"),false) as A2P_SavePlayerProfile;
            bool ok=first!=null&&repeat!=null&&conflict!=null&&first.Error==0&&repeat.Error==0&&first.Revision==repeat.Revision&&conflict.Error==ErrorCode.ERR_OnlineWorldOperationConflict;return new ServerTestCmdRunResult(true,ok,ok?ServerTestCmdResultCode.Success:ServerTestCmdResultCode.ExecutionFailed,$"first={first?.Error} repeat={repeat?.Error} conflict={conflict?.Error}");
        }
    }

    [ServerTestCmdHandler("server.world-old-unit-rejected", "world-old-unit-rejected", "server.general", 0, "服务器", 1000,
        "旧 Unit 拒绝", "$world-old-unit-rejected <unit-id> <lifecycle>", "测试旧 Unit 的移动、攻击和物资请求均被拒绝", "$world-old-unit-rejected <unit-id> <lifecycle>", "$world-old-unit-rejected 5 1")]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class WorldOldUnitRejectedTestCmdHandler:IServerTestCmdHandler
    {
        public async ETTask<ServerTestCmdRunResult> Run(Scene root,Session session,string argumentText)
        {
            string[] parts=(argumentText??string.Empty).Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length!=2||!long.TryParse(parts[0],out long oldUnit)||!long.TryParse(parts[1],out long lifecycle))return new ServerTestCmdRunResult(true,false,ServerTestCmdResultCode.InvalidInput,"参数无效");GatePlayerProxy proxy=session.GetComponent<OnlineSessionComponent>()?.Proxy;if(proxy==null||proxy.PlayerActorId==default)return new ServerTestCmdRunResult(true,false,ServerTestCmdResultCode.ExecutionFailed,"当前 Gate 会话未认证");
            G2P_GetCurrentWorld query=G2P_GetCurrentWorld.Create();query.PlayerId=proxy.PlayerId;query.SessionEpoch=proxy.SessionEpoch;using P2G_GetCurrentWorld current=await root.GetComponent<MessageSender>().Call(proxy.PlayerActorId,query,false) as P2G_GetCurrentWorld;if(current?.World==null)return new ServerTestCmdRunResult(true,false,ServerTestCmdResultCode.ExecutionFailed,"当前 World 不可用");ActorId world=root.GetOnlineSceneActorId("OnlineWorld");
            P2W_ReportWorldTransform move=P2W_ReportWorldTransform.Create();move.PlayerId=proxy.PlayerId;move.WorldId=current.World.WorldId;move.MapId=current.World.MapId;move.UnitId=oldUnit;move.PositionX=99;move.PositionZ=99;move.LifecycleRevision=lifecycle;using W2P_ReportWorldTransform moved=await root.GetComponent<MessageSender>().Call(world,move,false) as W2P_ReportWorldTransform;
            P2W_WorldMeleeAttack attack=P2W_WorldMeleeAttack.Create();attack.PlayerId=proxy.PlayerId;attack.WorldId=current.World.WorldId;attack.MapId=current.World.MapId;attack.UnitId=oldUnit;attack.AttackSequence=99;attack.AimZ=1;attack.LifecycleRevision=lifecycle;using W2P_WorldMeleeAttack attacked=await root.GetComponent<MessageSender>().Call(world,attack,false) as W2P_WorldMeleeAttack;
            P2W_OperateWorldInventory inventory=P2W_OperateWorldInventory.Create();inventory.PlayerId=proxy.PlayerId;inventory.WorldId=current.World.WorldId;inventory.MapId=current.World.MapId;inventory.UnitId=oldUnit;inventory.OperationId=$"old-unit-{oldUnit}";inventory.ExpectedInventoryRevision=current.World.SelfInventory.Revision;inventory.ActionKind=3;inventory.SourceContainerKind=3;inventory.TargetContainerKind=1;inventory.TargetSlotIndex=0;inventory.LifecycleRevision=lifecycle;using W2P_OperateWorldInventory operated=await root.GetComponent<MessageSender>().Call(world,inventory,false) as W2P_OperateWorldInventory;
            bool ok=moved!=null&&attacked!=null&&operated!=null&&moved.Error!=0&&attacked.Error!=0&&operated.Error!=0;return new ServerTestCmdRunResult(true,ok,ok?ServerTestCmdResultCode.Success:ServerTestCmdResultCode.ExecutionFailed,$"move={moved?.Error} attack={attacked?.Error} inventory={operated?.Error}");
        }
    }

    [ServerTestCmdHandler("server.world-stale-transform", "world-stale-transform", "server.general", 0, "服务器", 1000,
        "旧地图推送屏障", "$world-stale-transform <map-id> <unit-id>",
        "测试 Player Actor 在切图后丢弃旧地图 Transform 推送", "$world-stale-transform <map-id> <unit-id>",
        "$world-stale-transform map.another-day.mission-sample 1")]
    [FriendOf(typeof(OnlineSessionComponent))]
    [FriendOf(typeof(GatePlayerProxy))]
    public sealed class WorldStaleTransformTestCmdHandler: IServerTestCmdHandler
    {
        public async ETTask<ServerTestCmdRunResult> Run(Scene root, Session session, string argumentText)
        {
            string[] parts = (argumentText ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !long.TryParse(parts[1], out long oldUnitId) || oldUnitId <= 0)
                return new ServerTestCmdRunResult(true, false, ServerTestCmdResultCode.InvalidInput,
                    "用法：$world-stale-transform <map-id> <unit-id>");

            OnlineSessionComponent sessionComponent = session.GetComponent<OnlineSessionComponent>();
            GatePlayerProxy proxy = sessionComponent?.Proxy;
            if (proxy == null || proxy.IsDisposed || !proxy.IsAuthenticated || proxy.PlayerActorId == default)
                return new ServerTestCmdRunResult(true, false, ServerTestCmdResultCode.ExecutionFailed, "当前 Gate 会话未认证");

            G2P_GetCurrentWorld beforeRequest = G2P_GetCurrentWorld.Create();
            beforeRequest.PlayerId = proxy.PlayerId;
            beforeRequest.SessionEpoch = proxy.SessionEpoch;
            using P2G_GetCurrentWorld before = await root.GetComponent<MessageSender>().Call(proxy.PlayerActorId, beforeRequest, false) as P2G_GetCurrentWorld;
            if (before.Error != 0 || !before.HasWorld || before.World == null || string.Equals(before.World.MapId, parts[0], StringComparison.Ordinal))
                return new ServerTestCmdRunResult(true, false, ServerTestCmdResultCode.ExecutionFailed, "当前地图不是旧图之外的有效地图");

            W2P_WorldPlayerTransformChanged stale = W2P_WorldPlayerTransformChanged.Create();
            stale.PlayerId = proxy.PlayerId;
            stale.WorldId = before.World.WorldId;
            stale.MapId = parts[0];
            stale.WorldRevision = before.World.WorldRevision + 100;
            stale.Player = WorldPlayerInfo.Create();
            stale.Player.PlayerId = proxy.PlayerId;
            stale.Player.UnitId = oldUnitId;
            stale.Player.WorldId = before.World.WorldId;
            stale.Player.MapId = parts[0];
            root.GetComponent<MessageSender>().Send(proxy.PlayerActorId, stale);

            // 同一发送端向同一 Ordered Player Actor 邮箱投递 RPC；返回即证明前一条 W2P 已处理。
            G2P_GetCurrentWorld barrierRequest = G2P_GetCurrentWorld.Create();
            barrierRequest.PlayerId = proxy.PlayerId;
            barrierRequest.SessionEpoch = proxy.SessionEpoch;
            using P2G_GetCurrentWorld barrier = await root.GetComponent<MessageSender>().Call(proxy.PlayerActorId, barrierRequest, false) as P2G_GetCurrentWorld;
            if (barrier.Error != 0 || !barrier.HasWorld || barrier.World == null)
                return new ServerTestCmdRunResult(true, false, ServerTestCmdResultCode.ExecutionFailed, "Player Actor 屏障未返回当前地图");

            return new ServerTestCmdRunResult(true, true, ServerTestCmdResultCode.Success,
                $"{barrier.World.MapId}|{barrier.World.SelfUnitId}");
        }
    }
}
