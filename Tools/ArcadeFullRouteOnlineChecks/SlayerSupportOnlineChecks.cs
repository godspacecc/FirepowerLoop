using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

/// <summary>商店实例进入正式战斗字典后的发射、接触、接力与死亡检查。</summary>
internal static class SlayerSupportOnlineChecks
{
    private static ArcadeSlayerMartConfiguration Config=>ServerConfig.Instance.SlayerMart;
    public static int Run(int max)
    {
        var tests=new (string,Action<OnlineArcadeRunActor,OnlineArcadeBattleRuntime>)[]{
            ("Ordinary short barrel extends purchased lifetime without refilling charges",(a,b)=>{Grant(a,SlayerMartEffect.SawBarrel,1,300);var item=S(b).Supports.Single(p=>p.Kind==SlayerMartSupportKind.Barrel);item.RemainingSeconds=1;item.Charges=1;item.CooldownSeconds=.2;var drop=ServerConfig.Instance.Tables.TbArcadeDrop.DataList.Single(p=>p.ProductId==Config.Supports.Get(SlayerMartSupportKind.Barrel).ProductId);Require((bool)Call("ApplyDrop",a,b.Players[1],drop));Require(!b.Players[1].Powerups.ContainsKey(ArcadePowerupBehavior.Barrel)&&item.RemainingSeconds==Config.Supports.Get(SlayerMartSupportKind.Barrel).Duration&&item.Charges==1&&item.CooldownSeconds==.2);}),
            ("Living ordinary saw keeps working during teammate respawn",(a,b)=>{Ordinary(a,SlayerMartSupportKind.Saw);b.Players[2].RespawnSeconds=1;float phase=Config.Parameter("support.orbitSpeed")*.1f,r=Config.Supports.Get(SlayerMartSupportKind.Saw).Radius;var e=Enemy(b,1,MathF.Cos(phase)*r,MathF.Sin(phase)*r);Call("TickPowerups",a,b,.1f);Require(e.CurrentHealth<1000);}),
            ("Ordinary phases persist without reapplying barrel speed mastery",(a,b)=>{Ordinary(a,SlayerMartSupportKind.Barrel);S(b).Owned.Add("slayermart.item.22");Call("TickPowerups",a,b,1f);double phase=S(b).OrdinaryBarrelPhase;Require(Math.Abs(phase-Config.Parameter("support.orbitSpeed")*1.25)<.0001);ManagerCall("CaptureRoundCheckpoint",a.Record,b);var restored=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);Require(restored.SlayerMart.Players[0].OrdinaryBarrelPhase==phase);}),
            ("Ordinary mastered saw contacts opposite body",(a,b)=>{Ordinary(a,SlayerMartSupportKind.Saw);S(b).Owned.Add("slayermart.item.11");float r=Config.Supports.Get(SlayerMartSupportKind.Saw).Radius*2;var e=Enemy(b,1,-r,0);Call("TickPowerups",a,b,0f);Require(e.CurrentHealth<1000);}),
            ("Ordinary saw leaves empty ring center safe",(a,b)=>{Ordinary(a,SlayerMartSupportKind.Saw);var e=Enemy(b,1,0,0);Call("TickPowerups",a,b,0f);Require(e.CurrentHealth==1000);}),
            ("Ordinary saw shares target cooldown across bodies",(a,b)=>{Ordinary(a,SlayerMartSupportKind.Saw);S(b).Owned.Add("slayermart.item.11");float r=Config.Supports.Get(SlayerMartSupportKind.Saw).Radius*2;var e=Enemy(b,1,r,0);Call("TickPowerups",a,b,0f);int hp=e.CurrentHealth;Call("TickPowerups",a,b,0f);Require(hp<1000&&e.CurrentHealth==hp);}),
            ("Ordinary barrel triggers at real orbit body",(a,b)=>{Ordinary(a,SlayerMartSupportKind.Barrel);var power=b.Players[1].Powerups[ArcadePowerupBehavior.Barrel];Enemy(b,1,Config.Parameter("support.orbitRadius"),0);int charges=power.Charges;Call("TickPowerups",a,b,0f);Require(power.Charges==charges-1);}),
            ("Ordinary barrel cannot trigger at empty ring center",(a,b)=>{Ordinary(a,SlayerMartSupportKind.Barrel);var power=b.Players[1].Powerups[ArcadePowerupBehavior.Barrel];Enemy(b,1,0,0);int charges=power.Charges;Call("TickPowerups",a,b,0f);Require(power.Charges==charges);}),
            ("Three chickens emit three attributed sources",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300);Enemy(b,1,5,0);Step(a,0);Require(b.Projectiles.Count==3&&b.Projectiles.Values.All(p=>p.OwnerPlayerId==1)&&b.Projectiles.Values.Select(p=>p.SupportInstanceId).Distinct().Count()==3);}),
            ("Chicken clocks are independent",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300);Enemy(b,1,5,0);Step(a,0);Step(a,.1f);Require(b.Projectiles.Count==3&&S(b).Supports.All(p=>p.CooldownSeconds>0));}),
            ("Projectile cap consumes only successful chicken clocks",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300);Enemy(b,1,5,0);for(int i=0;i<ServerConfig.Instance.ArcadeRule.MaxProjectiles-1;i++)b.Projectiles[100000+i]=new(){RuntimeId=100000+i};Step(a,0);Require(b.Projectiles.Count==ServerConfig.Instance.ArcadeRule.MaxProjectiles&&S(b).Supports.Count(p=>p.CooldownSeconds>0)==1);}),
            ("Short ordinary chicken cannot replace purchased shares",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300);Require((bool)Call("ApplyDrop",a,b.Players[1],ChickenDrop()));Require(S(b).Supports.Count==3&&S(b).Supports.All(p=>p.RemainingSeconds==300));}),
            ("Ordinary chicken refuses sixth total slot",(a,b)=>{for(int k=0;k<5;k++)Grant(a,SlayerMartEffect.Chicken,1,300);Require(!(bool)Call("ApplyDrop",a,b.Players[1],ChickenDrop())&&!b.Players[1].Powerups.ContainsKey(ArcadePowerupBehavior.Chicken));}),
            ("Tesla bodies damage contact and leave ring center safe",(a,b)=>{Grant(a,SlayerMartEffect.TeslaPack,3);var near=AtBody(b,1);var center=Enemy(b,2,0,0);Step(a,0);Require(near.CurrentHealth==1000-Config.Supports.Get(SlayerMartSupportKind.Orb).Damage&&center.CurrentHealth==1000);}),
            ("Tesla group cannot triple hit same enemy",(a,b)=>{Grant(a,SlayerMartEffect.TeslaPack,3);foreach(var i in S(b).Supports)i.Phase=0;var e=AtBody(b,1);Step(a,0);Require(e.CurrentHealth==1000-Config.Supports.Get(SlayerMartSupportKind.Orb).Damage);b.RoundGeneration++;foreach(var player in b.Players.Values)player.ReadyRoundGeneration=b.RoundGeneration;b.Enemies.Clear();b.EnemyIterationIds.Clear();var next=AtBody(b,1);Step(a,0);Require(next.CurrentHealth==1000-Config.Supports.Get(SlayerMartSupportKind.Orb).Damage);}),
            ("Tesla targets have separate cooldowns",(a,b)=>{Grant(a,SlayerMartEffect.TeslaPack,3);var e=AtBody(b,1);var other=Enemy(b,2,e.PositionX,e.PositionZ);Step(a,0);Require(e.CurrentHealth==other.CurrentHealth&&e.CurrentHealth<1000);}),
            ("Boss contact takes damage without knockback",(a,b)=>{Grant(a,SlayerMartEffect.TeslaPack,3);var e=AtBody(b,1);e.ContentBossKind=1;e.MoveSpeed=3;float x=e.PositionX,z=e.PositionZ;Step(a,0);Require(e.CurrentHealth<1000&&e.PositionX==x&&e.PositionZ==z);}),
            ("Combo barrel exhausts after three real triggers",(a,b)=>{Grant(a,SlayerMartEffect.SawBarrel,1,300);var barrel=S(b).Supports.Single(p=>p.Kind==SlayerMartSupportKind.Barrel);var e=AtBody(b,1,barrel);for(int k=0;k<3;k++){float dt=k==0?0:.45f;MoveToUpcoming(b,e,barrel,dt);Step(a,dt);}Require(barrel.Charges==0&&barrel.RemainingSeconds==0);Step(a,0);Require(S(b).Supports.All(p=>p.Kind!=SlayerMartSupportKind.Barrel));}),
            ("Relay advances sequential identity and retains queue",(a,b)=>{Grant(a,SlayerMartEffect.BarrelRelay,3);var first=S(b).Supports[0];var e=AtBody(b,1,first);for(int k=0;k<3;k++){float dt=k==0?0:.45f;MoveToUpcoming(b,e,first,dt);Step(a,dt);}Step(a,0);Require(S(b).Supports.Count==1&&S(b).Supports[0].InstanceId!=first.InstanceId&&S(b).Supports[0].Charges==3&&S(b).Queues[0].Waiting.Count==1);}),
            ("Round transition freezes purchased clock",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300);b.RoundTransitionPending=true;Step(a,5);Require(S(b).Supports.All(p=>p.RemainingSeconds==300)&&b.Projectiles.Count==0);}),
            ("Open store freezes purchased clock",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300);b.SlayerMart.Ready=true;b.SlayerMart.Closing=false;Step(a,5);Require(S(b).Supports.All(p=>p.RemainingSeconds==300));}),
            ("Respawn barrier freezes whole team support clock",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300);b.Players[2].RespawnSeconds=1;Step(a,5);Require(S(b).Supports.All(p=>p.RemainingSeconds==300));}),
            ("Round capture restores spent charges and phase",(a,b)=>{Grant(a,SlayerMartEffect.BarrelRelay,3);Step(a,1);S(b).Supports[0].Charges=1;ManagerCall("CaptureRoundCheckpoint",a.Record,b);var restored=OnlineArcadeRunActorSystem.PrepareBattle(a.Record,false);Require(restored.SlayerMart.Players[0].Supports[0].Charges==1&&restored.SlayerMart.Players[0].Supports[0].Phase==S(b).Supports[0].Phase&&restored.SlayerMart.Players[0].Queues[0].Waiting.Count==2);}),
            ("Real death clears supports queues and ordinary support slots",(a,b)=>{Grant(a,SlayerMartEffect.BarrelRelay,3);S(b).VestSlots=2;S(b).Owned.Add("slayermart.item.16");S(b).StormCharge=99;foreach(var kind in new[]{ArcadePowerupBehavior.Chicken,ArcadePowerupBehavior.Saw,ArcadePowerupBehavior.Barrel})b.Players[1].Powerups[kind]=new(){Behavior=kind,RemainingSeconds=10};b.Players[1].CurrentHealth=10;Call("DamagePlayerCore",a,b,b.Players[1],20,SlayerMartHitKind.Ordinary);Require(S(b).Supports.Count==0&&S(b).Queues.Count==0&&b.Players[1].Powerups.Count==0&&S(b).VestSlots==2&&S(b).StormCharge==99);}),
            ("Teammate packages emit only their own source owner",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300,2);Enemy(b,1,5,0);Step(a,0);Require(b.Projectiles.Count==3&&b.Projectiles.Values.All(p=>p.OwnerPlayerId==2)&&S(b).Supports.Count==0);}),
            ("Expired purchased chickens free capacity immediately",(a,b)=>{Grant(a,SlayerMartEffect.ChickenPack,3,300);Step(a,301);Require(S(b).Supports.Count==0&&S(b).Chickens==0);})
        };
        if(tests.Length>max)throw new InvalidOperationException("Test limit exceeded");int failed=0;
        foreach(var(name,test)in tests)try
        {
            var r=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,IsCasualRoute=true,ArenaConfigNumber=2,NextArena=2,CompletedArena=1,ConfigFingerprint=ServerConfig.Instance.ArcadeRule.ConfigFingerprint,
                SlayerMartRulesVersion=1,SlayerMart=new(){RegionInstanceId="1:slayermart:1",Visit=1,Players=new(){new(){PlayerId=1},new(){PlayerId=2}}},Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"},new(){PlayerId=2,Lives=3,Ready=true,WeaponId="arcade.weapon.basic"}}};
            var b=OnlineArcadeRunActorSystem.PrepareBattle(r,false);var a=new OnlineArcadeRunActor{Record=r,Battle=b};foreach(var p in b.Players.Values){p.PositionX=p.PositionZ=0;p.ReadyRoundGeneration=b.RoundGeneration;}
            test(a,b);Console.WriteLine("PASS SlayerMartSupportOnline."+name);
        }catch(Exception ex){failed++;Console.WriteLine("FAIL SlayerMartSupportOnline."+name+": "+(ex.InnerException??ex).Message);}
        Console.WriteLine($"{tests.Length-failed}/{tests.Length} passed");return failed==0?0:1;
    }
    private static ArcadeSlayerMartPlayer S(OnlineArcadeBattleRuntime b,long id=1)=>b.SlayerMart.Players.Single(p=>p.PlayerId==id);
    private static void Grant(OnlineArcadeRunActor a,SlayerMartEffect effect,int amount,float duration=0,long owner=1)
    {var s=a.Battle.SlayerMart;s.PendingGrants.Add(new(){EventId=s.RegionInstanceId+":"+(++s.GrantSequence),PlayerId=owner,Effect=effect,Amount=amount,Duration=duration});Require(ArcadeSlayerMartSupportRules.TryApplyGrants(s,Config,new Dictionary<long,int>(),out var next,out var error),error);a.Battle.SlayerMart=next;}
    private static void Ordinary(OnlineArcadeRunActor a,SlayerMartSupportKind kind)
    {var drop=ServerConfig.Instance.Tables.TbArcadeDrop.DataList.Single(p=>p.ProductId==Config.Supports.Get(kind).ProductId);Require((bool)Call("ApplyDrop",a,a.Battle.Players[1],drop));}
    private static ArcadeDropConfig ChickenDrop()=>ServerConfig.Instance.Tables.TbArcadeDrop.DataList.Single(p=>p.ProductId==Config.Supports.Get(SlayerMartSupportKind.Chicken).ProductId);
    private static OnlineArcadeEnemyRuntime Enemy(OnlineArcadeBattleRuntime b,long id,float x,float z)
    {var e=new OnlineArcadeEnemyRuntime{RuntimeId=id,PositionX=x,PositionZ=z,CurrentHealth=1000,MaxHealth=1000};b.Enemies.Add(id,e);b.EnemyIterationIds.Add(id);return e;}
    private static OnlineArcadeEnemyRuntime AtBody(OnlineArcadeBattleRuntime b,long id,ArcadeSlayerMartEffectInstance i=null)
    {var p=S(b);i??=p.Supports[0];var pos=ArcadeSlayerMartSupportRules.Bodies(p,i,0,0,Config.Supports,Config.Parameter)[0];return Enemy(b,id,pos.X,pos.Y);}
    private static void MoveToUpcoming(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e,ArcadeSlayerMartEffectInstance i,float seconds)
    {double phase=i.Phase;i.Phase+=Config.Parameter("support.orbitSpeed")*seconds;var point=ArcadeSlayerMartSupportRules.Bodies(S(b),i,0,0,Config.Supports,Config.Parameter)[0];i.Phase=phase;e.PositionX=point.X;e.PositionZ=point.Y;}
    private static void Step(OnlineArcadeRunActor a,float seconds)=>Call("TickSlayerMartSupports",a,a.Battle,seconds);
    private static object Call(string method,params object[] args)
    {var m=typeof(OnlineArcadeRunActorSystem).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic);if(m==null)throw new InvalidOperationException("Missing runtime support entry: "+method);return m.Invoke(null,args);}
    private static object ManagerCall(string method,params object[] args)=>typeof(OnlineArcadeRunManagerSystem).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
    private static void Require(bool value,string error=null){if(!value)throw new InvalidOperationException(error??"Runtime support contract violated");}
}
