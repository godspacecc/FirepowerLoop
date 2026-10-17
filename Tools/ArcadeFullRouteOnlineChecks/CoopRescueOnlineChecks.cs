using System.Reflection;
using ET;
using ET.Server;

/// <summary>仅执行双人救援权威死亡、计时与轮节点的明确范围，不启动服务器或全量测试。</summary>
internal static class CoopRescueOnlineChecks
{
    static object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem).GetMethod(name, BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,values)!;
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static float Down(OnlineArcadeBattlePlayerRuntime p)
    {
        var field=p.GetType().GetField("DownedSeconds");Require(field!=null,"missing independent downed timer");return (float)field!.GetValue(p)!;
    }
    internal static OnlineArcadeRunActor Actor()
    {
        var r=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,IsCasualRoute=true,ArenaConfigNumber=3,NextArena=2,CompletedArena=1,
            ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,Players=new(){new(){PlayerId=1,Lives=1,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"}}};
        var b=OnlineArcadeRunActorSystem.PrepareBattle(r,false);
        foreach(var p in b.Players.Values){p.ReadyRoundGeneration=b.RoundGeneration;p.InvulnerabilitySeconds=0;p.LastInputAt=TimeInfo.Instance.ServerNow();}
        b.Players[2].PositionX=b.Players[1].PositionX+10;return new(){Record=r,Battle=b};
    }
    static void Near(OnlineArcadeRunActor a){a.Battle.Players[2].PositionX=a.Battle.Players[1].PositionX;a.Battle.Players[2].PositionZ=a.Battle.Players[1].PositionZ;}
    internal static void Kill(OnlineArcadeRunActor a,long id=1)=>Call("DamagePlayerCore",a,a.Battle,a.Battle.Players[id],100000,FirepowerLoop.Arcade.SlayerMartHitKind.Ordinary);
    internal static void Tick(OnlineArcadeRunActor a,float seconds)=>Call("TickPlayers",a,a.Battle,seconds,TimeInfo.Instance.ServerNow());
    internal static OnlineArcadeRunRecord Capture(OnlineArcadeRunActor a)
    {
        var r=OnlineArcadeRunManagerSystem.Clone(a.Record);
        typeof(OnlineArcadeRunManagerSystem).GetMethod("CaptureRoundCheckpoint",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{r,a.Battle});return r;
    }
    public static int Run(int maximum)
    {
        var tests=new (string,Action)[]{
            ("Zero life death starts independent60 seconds",()=>{var a=Actor();Kill(a);var p=a.Battle.Players[1];Require(!p.Alive&&a.Record.Players[0].Lives==0&&Down(p)==60&&p.RespawnSeconds==0&&!a.Battle.AllPlayersDefeated,"zero-life death contract");}),
            ("Base timer restores full health without granting life",()=>{var a=Actor();Kill(a);Tick(a,59);Require(!a.Battle.Players[1].Alive&&Down(a.Battle.Players[1])==1,"early recovery");Tick(a,1);var p=a.Battle.Players[1];Require(p.Alive&&p.CurrentHealth==p.MaxHealth&&Down(p)==0&&a.Record.Players[0].Lives==0&&p.InvulnerabilitySeconds>0,"recovery granted life or lost protection");}),
            ("Recovered zero-life member can die into a new countdown",()=>{var a=Actor();Kill(a);Tick(a,60);a.Battle.Players[1].InvulnerabilitySeconds=0;Kill(a);Require(!a.Battle.Players[1].Alive&&Down(a.Battle.Players[1])==60&&a.Record.Players[0].Lives==0,"repeat death underflow or permanent spectator");}),
            ("Paid short respawn remains separate",()=>{var a=Actor();a.Record.Players[0].Lives=2;Kill(a);Require(a.Battle.Players[1].RespawnSeconds==ServerConfig.Instance.ArcadeRule.RespawnDelaySeconds&&Down(a.Battle.Players[1])==0,"paid respawn replaced");Tick(a,2);Require(a.Battle.Players[1].Alive&&a.Record.Players[0].Lives==1,"paid respawn broken");}),
            ("All zero-life downed members fail immediately",()=>{var a=Actor();a.Record.Players[1].Lives=1;Kill(a);Kill(a,2);Require(a.Battle.AllPlayersDefeated,"timers incorrectly prevent team defeat");Tick(a,60);Require(a.Battle.Players.Values.All(p=>!p.Alive),"failed battle resurrected");}),
            ("Downed timer survives round-node clone and restore",()=>{var a=Actor();Kill(a);Tick(a,17);var r=Capture(a);var b=OnlineArcadeRunActorSystem.PrepareBattle(OnlineArcadeRunManagerSystem.Clone(r),false);Require(!b.Players[1].Alive&&Down(b.Players[1])==43&&b.Players[1].RespawnSeconds==0,"round node reset downed state");}),
            ("Recovered zero-life identity survives round restore",()=>{var a=Actor();Kill(a);Tick(a,60);var b=OnlineArcadeRunActorSystem.PrepareBattle(Capture(a),false);Require(b.Players[1].Alive&&b.Players[1].CurrentHealth==100&&Down(b.Players[1])==0,"zero life active member lost");}),
            ("Both recovered zero-life members may restore",()=>{var a=Actor();Kill(a);Tick(a,60);a.Record.Players[1].Lives=1;Kill(a,2);Tick(a,60);var b=OnlineArcadeRunActorSystem.PrepareBattle(Capture(a),false);Require(b.Players.Values.All(p=>p.Alive)&&!b.AllPlayersDefeated,"lives-only entrance rejected active team");}),
            ("Challenge death never enters casual rescue",()=>{var a=Actor();a.Battle.IsChallenge=true;Kill(a);Require(!a.Battle.Players[1].Alive&&Down(a.Battle.Players[1])==0&&a.Record.Players[0].Lives==1,"challenge consumed life or entered rescue");}),
            ("Nearby living helper doubles recovery rate",()=>{var a=Actor();Kill(a);Near(a);Tick(a,10);Require(Down(a.Battle.Players[1])==40,"nearby helper did not accelerate");}),
            ("Radius includes boundary and excludes outside",()=>{var a=Actor();Kill(a);Near(a);a.Battle.Players[2].PositionX+=2;Tick(a,1);Require(Down(a.Battle.Players[1])==58,"boundary excluded");a.Battle.Players[2].PositionX+=.01f;Tick(a,1);Require(Down(a.Battle.Players[1])==57,"outside still helped");}),
            ("Leaving radius preserves earned progress",()=>{var a=Actor();Kill(a);Near(a);Tick(a,5);a.Battle.Players[2].PositionX+=10;Tick(a,5);Require(Down(a.Battle.Players[1])==45,"leaving reset or retained speed");}),
            ("Current obstacle blocks nearby helper",()=>{var a=Actor();Kill(a);var o=a.Battle.RoundLayout.Obstacles[0];var p=a.Battle.Players[1];var h=a.Battle.Players[2];p.PositionX=o.X+o.HalfWidth+.1f;p.PositionZ=o.Y+o.HalfHeight-.2f;h.PositionX=o.X+o.HalfWidth-.2f;h.PositionZ=o.Y+o.HalfHeight+.1f;Tick(a,1);Require(Down(p)==59,"help passed through wall");}),
            ("Treasure helper cannot accelerate outside member",()=>{var a=Actor();Kill(a);Near(a);a.Battle.Players[2].InTreasureRoom=true;Tick(a,10);Require(Down(a.Battle.Players[1])==50&&!a.Battle.AllPlayersDefeated,"cross-region rescue or false defeat");}),
            ("Disconnected player freezes recovery",()=>{var a=Actor();Kill(a);a.DisconnectedAtByPlayer[2]=1;Tick(a,10);Require(Down(a.Battle.Players[1])==60,"disconnect consumed timer");}),
            ("Unacknowledged map freezes recovery",()=>{var a=Actor();Kill(a);a.Battle.Players[2].ReadyRoundGeneration=-1;Tick(a,10);Require(Down(a.Battle.Players[1])==60,"loading consumed timer");}),
            ("Shop phase freezes ordinary recovery",()=>{var a=Actor();Kill(a);a.Battle.SlayerMartReturnWave=3;Tick(a,10);Require(Down(a.Battle.Players[1])==60,"shop consumed timer");}),
            ("Exit waiting permits nearby recovery",()=>{var a=Actor();Kill(a);Near(a);a.Battle.AwaitingExit=true;Tick(a,30);Require(a.Battle.Players[1].Alive&&Down(a.Battle.Players[1])==0,"exit prevented recovery");}),
            ("All treasure members freeze ordinary recovery",()=>{var a=Actor();Kill(a);foreach(var p in a.Battle.Players.Values)p.InTreasureRoom=true;Tick(a,10);Require(Down(a.Battle.Players[1])==60,"all treasure consumed timer");}),
            ("Round transition freezes independent timer",()=>{var a=Actor();Kill(a);a.Battle.RoundTransitionPending=true;Tick(a,10);Require(Down(a.Battle.Players[1])==60,"transition consumed timer");}),
            ("Downed marker locates recovery body without creating damage",()=>{var a=Actor();Kill(a);using var snapshot=(OnlineArcadeBattleInfo)Call("BuildBattleSnapshot",a.Battle);var marker=snapshot.ContentAreas.SingleOrDefault(p=>p.Kind==12);Require(marker!=null&&marker.Warning&&marker.X==a.Battle.Players[1].PositionX&&marker.Radius==2&&!a.Battle.ContentAreas.Any(p=>p.Kind==12),"missing or harmful rescue marker");}),
            ("Shop next-round life replaces zero-life countdown",()=>{
                var a=Actor();Kill(a);var r=Capture(a);r.NextArena=3;r.CompletedArena=2;r.RoundNumber=4;
                var state=FirepowerLoop.Arcade.ArcadeSlayerMartRules.Create("1",1,new long[]{1,2},17,ServerConfig.Instance.SlayerMart.Catalog);
                state.Ready=state.Closing=true;state.PendingGrants.Add(new(){PlayerId=1,Effect=FirepowerLoop.Arcade.SlayerMartEffect.NextRoundLife,Amount=1,EventId=state.RegionInstanceId+":"+(++state.GrantSequence)});r.SlayerMart=state;
                Require(OnlineArcadeRunActorSystem.ApplySlayerMartRoundCandidate(r,out var error),error??"life grant failed");
                Require(r.Players[0].Lives==1&&r.Players[0].RoundDownedSeconds==0&&r.Players[0].RoundRespawnSeconds>0,"life grant kept zero-life downed identity");
            }),
            ("Zero-life active eligibility reaches safe-node snapshot",()=>{var a=Actor();Kill(a);Tick(a,60);a.Record=Capture(a);using var snapshot=a.BuildSnapshot();var p=snapshot.Players.First(p=>p.PlayerId==1);Require(p.GetType().GetProperty("CanReadyForArena")?.GetValue(p) is true,"safe-node readiness identity missing");}),
            ("Recovered transit member does not need old safe-node ready bit",()=>{var a=Actor();Kill(a);a.Record.Players[0].Ready=false;a.Record.Phase=OnlineArcadeRunPhase.SafeCheckpoint;Tick(a,60);var b=OnlineArcadeRunActorSystem.PrepareBattle(Capture(a),false,false);Require(b.Players[1].Alive,"internal round blocked by stale ready bit");}),
            ("Actual round transaction ignores stale ready and writes recoverable zero-life identity",()=>{
                string prefix=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CoopRescueOnline"));string root=System.IO.Path.Combine(prefix,Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(root);
                try{
                    var a=Actor();Kill(a);a.Record.Players[0].Ready=false;Tick(a,60);a.Record=Capture(a);a.Record.Phase=OnlineArcadeRunPhase.SafeCheckpoint;a.CommittedRecord=OnlineArcadeRunManagerSystem.Clone(a.Record);
                    typeof(Entity).GetProperty("InstanceId")!.SetValue(a,1L);var manager=new OnlineArcadeRunManagerComponent{StorePath=System.IO.Path.Combine(root,"runs.json")};
                    var flags=BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
                    typeof(OnlineArcadeRunManagerSystem).GetMethod("CommitWithCandidate",flags)!.Invoke(null,new object[]{manager,a.Record,0L});manager.Runs.Add(1,a);
                    a.Battle.RoundTransitionPending=true;
                    Require((bool)typeof(OnlineArcadeRunManagerSystem).GetMethod("TryCommitRoundTransition",flags)!.Invoke(null,new object[]{manager,a})!,"real round transaction frozen: "+a.Battle.RoundLoadError);
                    var saved=System.Text.Json.JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(System.IO.File.ReadAllText(manager.StorePath))!.Runs.Single();
                    Require(a.Battle.WaveIndex==1&&a.Battle.Players[1].Alive&&saved.Players[0].RoundAlive&&saved.Players[0].Lives==0,"real saved node lost zero-life identity");
                }finally{Require(System.IO.Path.GetFullPath(root).StartsWith(prefix+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"unsafe scratch root");System.IO.Directory.Delete(root,true);}
            }),
            ("Active zero-life member keeps normal new-arena empty refill",()=>{var a=Actor();Kill(a);Tick(a,60);var r=Capture(a);r.RoundLayoutId=string.Empty;r.Players[0].Bombs=r.Players[0].Dashes=0;OnlineArcadeRunActorSystem.PrepareBattle(r,false);Require(r.Players[0].Bombs==1&&r.Players[0].Dashes==1,"active member excluded from normal next-arena refill");}),
            ("Delayed island kill life gain converts downed state",()=>{
                var a=Actor();a.Record.ArenaConfigNumber=1;a.Battle=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);foreach(var p in a.Battle.Players.Values)p.InvulnerabilitySeconds=0;
                Kill(a);var rules=FirepowerLoop.Arcade.ArcadeTreasureRoomParameters.FromConfig(ServerConfig.Instance.Tables.TbArcadeTreasureRoom.DataList.First());a.Record.Players[0].Score=rules.LifeThreshold-100;
                Call("AwardIslandKill",a,a.Battle,new OnlineArcadeEnemyRuntime{LastDamagePlayerId=1});
                Require(a.Record.Players[0].Lives==1&&Down(a.Battle.Players[1])==0&&a.Battle.Players[1].RespawnSeconds>0,"delayed life gain kept invalid downed state");
            })
        };
        if(tests.Length>maximum)throw new ArgumentException("Declared maximum smaller than actual scope");int failed=0;
        foreach(var(name,test)in tests)try{test();Console.WriteLine("PASS CoopRescueOnline."+name);}catch(Exception e){failed++;Console.WriteLine("FAIL CoopRescueOnline."+name+": "+(e.InnerException??e).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
}
