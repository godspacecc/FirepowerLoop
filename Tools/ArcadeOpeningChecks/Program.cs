using System.Reflection;
using ET;
using ET.Server;
using FirepowerLoop.Arcade;

// 独立进程只检查正式服务端权威路径；不监听网络，不读写玩家存档。
var repository = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var config = ServerConfig.CreateForValidation("OnlineLocal", name => File.ReadAllBytes(
    Path.Combine(repository,"Server/GameServer/Config/Luban",name+".bytes")));
World.Instance.AddSingleton(config); World.Instance.AddSingleton<TimeInfo>();
void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
object Call(string method,params object[] values)=>typeof(OnlineArcadeRunActorSystem)
    .GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values)!;
int passed=0;
void Check(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
OnlineArcadeRunRecord Record()=>new(){RunId=1,RoomId=2,Revision=1,NextArena=1,ArenaConfigNumber=1,
    ConfigFingerprint=config.ArcadeRule.ConfigFingerprint,
    IsCasualRoute=true,Phase=OnlineArcadeRunPhase.SafeCheckpoint,Players=new(){
        new(){PlayerId=1,Lives=3,Bombs=0,Dashes=4,Ready=true,WeaponId=config.ArcadeRule.BaseWeaponId},
        new(){PlayerId=2,Lives=3,Bombs=2,Dashes=0,Ready=true,WeaponId=config.ArcadeRule.BaseWeaponId}}};
Check("Dual opening queues 160/170/180/190 under the global alive budget",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    for(int i=0;i<4;i++){
        Call("PrepareWave",battle,config.Tables.TbArcadeArena.Get(1),i);
        Require(battle.TotalInWave==160+10*i,"wrong total "+i);
        Require(battle.SpawnQueue.All(e=>e=="enemy.arcade.basic"),"opening ranged enemy");
        Require(battle.MaxAlive<=config.ArcadeRule.MaxAliveEnemies,"alive cap exceeded");
    }
    Require(record.Players[0].Bombs==1&&record.Players[0].Dashes==4&&record.Players[1].Dashes==1,"opening resource refill");
});
Check("Queue prevents clear; completed round requires a confirmed exit",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};
    Require(!(bool)Call("TryAdvanceWave",actor,battle,200f),"pending queue was ignored");
    battle.SpawnQueueIndex=battle.SpawnQueue.Count;battle.Players[1].Firing=true;battle.Players[2].Firing=true;
    Call("TryAdvanceWave",actor,battle,0f);Call("TryAdvanceWave",actor,battle,2f);
    Require(battle.WaveIndex==0&&battle.AwaitingExit,"held attack skipped exit confirmation");
    battle.Players[1].Firing=false;Call("TryAdvanceWave",actor,battle,.1f);
    record.Players[0].Bombs=0;battle.Players[1].Firing=true;Call("TryAdvanceWave",actor,battle,.1f);
    Require(battle.WaveIndex==0&&!battle.RoundTransitionPending,"firing bypassed confirmed exit");
    var player=battle.Players[1];player.PositionX=battle.RoundLayout.ExitX;player.PositionZ=battle.RoundLayout.ExitY;
    player.ConfirmedExitTarget=1;
    Require((bool)Call("TickRoundExit",battle)&&battle.RoundTransitionPending,"confirmed exit did not request next round");
});
// 首关的节奏、同屏容量和满额后的补位均通过正式服务端生成入口验证。
Check("First round keeps 450ms cadence and refills the 24-enemy alive budget",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};
    battle.NextSpawnAt=0;
    Require((bool)Call("TrySpawnEnemy",battle,10000L),"first spawn missing");
    Require(!(bool)Call("TrySpawnEnemy",battle,10449L),"spawn interval shortened");
    Require((bool)Call("TrySpawnEnemy",battle,10450L),"450ms spawn missing");
    long now=10450L;
    for(int i=2;i<24;i++){now+=450;Require((bool)Call("TrySpawnEnemy",battle,now),"24-enemy budget not filled");}
    Require(battle.Enemies.Count==24&&battle.SpawnQueueIndex==24&&battle.TotalInWave==160,"wrong initial budget");
    now+=450;
    Require(!(bool)Call("TrySpawnEnemy",battle,now)&&battle.SpawnQueueIndex==24,"full budget discarded queued enemy");
    Call("KillEnemy",actor,battle,battle.Enemies.Values.First(),true);
    Require((bool)Call("TrySpawnEnemy",battle,now)&&battle.Enemies.Count==24&&battle.SpawnQueueIndex==25,"killed enemy was not replaced");
});
Check("Island waves preserve continuous entries and exhaust the original queue",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};
    foreach(var player in battle.Players.Values)player.InvulnerabilitySeconds=1000f;
    var rule=config.ArcadeRule;var arena=config.Tables.TbArcadeArena.Get(1);
    var p=ArcadeOpeningParameters.FromConfig(arena);
    for(int wave=0;wave<4;wave++){
        // 此检查直接准备各轮现场；上一轮已确认的出口状态不属于下一轮。
        battle.AwaitingExit=false;battle.RoundTransitionPending=false;
        battle.Enemies.Clear();Call("PrepareWave",battle,arena,wave);
        int total=battle.TotalInWave;
        int firstEntries=wave==0?4:2;
        for(int i=0;i<firstEntries;i++){battle.NextSpawnAt=0;Require((bool)Call("TrySpawnEnemy",battle,10000L),"missing spawn");}
        var region=new ArcadeIslandRegion(rule.ArenaHalfWidth,rule.ArenaHalfHeight,wave+1);
        var entries=battle.Enemies.Values.OrderBy(enemy=>enemy.RuntimeId).ToArray();
        var water=entries[0];var shore=entries[wave==0?2:1];
        Require(entries.All(enemy=>!region.Contains(enemy.PositionX,enemy.PositionZ)),"spawn must start in deep sea");
        if(wave==0){
            Require(entries[0].PositionZ < -10&&entries[1].PositionX > 10&&
                entries[2].PositionZ > 10&&entries[3].PositionX < -10,"first round must use all four entrances");
            Require(p.EnemySeaApproachDistance==6,"first island must retain six metres of deep-sea approach");
        }
        float x=water.PositionX,z=water.PositionZ;
        Call("TickEnemies",actor,battle,.05f);
        Require(MathF.Sqrt((water.PositionX-x)*(water.PositionX-x)+(water.PositionZ-z)*(water.PositionZ-z))<=water.MoveSpeed*.05f+.001f,"entry snapped");
        var player=battle.Players[1];player.PositionX=water.PositionX;player.PositionZ=water.PositionZ+.25f;
        int health=player.CurrentHealth;player.InvulnerabilitySeconds=0;water.AttackCooldownSeconds=0;
        Call("TickEnemies",actor,battle,.05f);
        Require(player.CurrentHealth<health,"water-side enemy lost ordinary contact attack");
        player.InvulnerabilitySeconds=1000f;player.PositionX=0;player.PositionZ=0;
        foreach(var member in battle.Players.Values){member.PositionX=0;member.PositionZ=0;}
        for(int i=0;i<500;i++)Call("TickEnemies",actor,battle,.05f);
        Require(!region.IsShallow(water.PositionX,water.PositionZ) &&
            region.Contains(shore.PositionX,shore.PositionZ),$"chase never reached sand wave={wave} water=({water.PositionX},{water.PositionZ}) shallow={region.IsShallow(water.PositionX,water.PositionZ)} shore=({shore.PositionX},{shore.PositionZ}) room={player.InTreasureRoom}");
        int spawned=firstEntries;
        while(battle.SpawnQueueIndex<battle.SpawnQueue.Count){
            battle.Enemies.Clear();battle.NextSpawnAt=0;
            Require((bool)Call("TrySpawnEnemy",battle,10000L),"queue stuck");spawned++;
            Require(battle.Enemies.Count==1,"spawn count wrong");
            var enemy=battle.Enemies.Values.Single();
            bool correctSide=wave==0 ? (((spawned-1)%4) switch {
                0=>enemy.PositionZ < -10,1=>enemy.PositionX > 10,2=>enemy.PositionZ > 10,_=>enemy.PositionX < -10
            }) : (enemy.PositionZ<0)==((spawned&1)==1);
            Require(correctSide,"entrance selection wrong");
        }
        Require(spawned==total&&total==160+wave*10,"wave count changed");
        battle.Enemies.Clear();
        if(wave==2){
            // 与第三轮可见入口重合的可达地面；不把玩家直接放到岛外配置坐标。
            foreach(var member in battle.Players.Values){member.PositionX=-12;member.PositionZ=-12;}
            Call("TickTreasureRoom",actor,battle,0f);Require(battle.Players.Values.All(p=>p.InTreasureRoom),"入口接触后未进入宝藏房");
            Call("TickTreasureRoom",actor,battle,60f);Require(battle.WaveIndex==3,"room did not return to fourth wave");}
        else {Call("TryAdvanceWave",actor,battle,0f);Require(battle.WaveClearAwarded,"clear blocked");}
    }
});
Check("Fourth round fire uses health damage and protection; room is isolated",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};var player=battle.Players[1];
    var p=ArcadeOpeningParameters.FromConfig(config.Tables.TbArcadeArena.Get(1));
    player.PositionX=p.CampfireX;player.PositionZ=p.CampfireY;battle.WaveIndex=2;
    Call("TickOpeningFire",actor,battle,1f);Require(player.CurrentHealth==100,"early fire damage");
    battle.WaveIndex=3;Call("TickOpeningFire",actor,battle,1f);Require(player.CurrentHealth==88,"missing continuous damage");
    Call("TickOpeningFire",actor,battle,1f);Require(player.CurrentHealth==88,"ignored immunity");
    player.InvulnerabilitySeconds=0;player.InTreasureRoom=true;Call("TickOpeningFire",actor,battle,1f);
    Require(player.CurrentHealth==88,"cross-region fire");
});
Check("Water slows authoritative movement",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};var player=battle.Players[1];
    var region=new ArcadeIslandRegion(config.ArcadeRule.ArenaHalfWidth,config.ArcadeRule.ArenaHalfHeight,1);
    int south=ArcadeIslandRegion.Samples*3/4;
    player.PositionX=0;player.PositionZ=(region.Outer[south].Y+region.Sand[south].Y)*.5f;
    player.MoveX=1;player.LastInputAt=TimeInfo.Instance.ServerNow();
    Call("TickPlayers",actor,battle,1f,player.LastInputAt);
    Require(Math.Abs(player.PositionX-config.ArcadeRule.PlayerMoveSpeed*.55f)<.001f,"water authority mismatch");
});
Check("Ordinary treasure adds personal score without cash",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};
    for(int i=0;i<11;i++)actor.ApplyDropForTests(battle.Players[1],"arcade.drop.gold.small");
    Require(record.Players[0].Score==1200&&record.Players[0].ScoreMultiplier==2&&record.Players[0].RunGold==0,"wrong treasure transaction");
    Require(record.Players[1].Score==0,"shared score leaked");
});
Check("Island player kill counts once with multiplier and life threshold without treasure progress",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};var player=record.Players[0];
    player.Score=199950;player.ScoreMultiplier=2;player.MultiplierProgress=4;
    var enemy=new OnlineArcadeEnemyRuntime{RuntimeId=1,PositionX=0,PositionZ=0,CurrentHealth=1,LastDamagePlayerId=1};
    battle.Enemies.Add(1,enemy);
    for(int i=0;i<config.ArcadeRule.MaxDrops;i++)battle.Drops.Add(i,new OnlineArcadeDropRuntime());
    Call("KillEnemy",actor,battle,enemy,false);
    Require(player.Score==200150&&player.Lives==4&&player.MultiplierProgress==4,"wrong kill score state");
    Call("KillEnemy",actor,battle,enemy,false);Require(player.Score==200150,"repeat score");
    var hazard=new OnlineArcadeEnemyRuntime{RuntimeId=2,LastDamagePlayerId=1};battle.Enemies.Add(2,hazard);
    Call("KillEnemy",actor,battle,hazard,true);Require(player.Score==200150&&record.Players[1].Score==0,"hazard or teammate score leaked");
});
Check("Bomb protection lasts three seconds and invalid uses do not grant it",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};var player=battle.Players[1];
    record.Players[0].Bombs=1;player.InvulnerabilitySeconds=5;Call("UseBomb",actor,battle,player);
    Require(player.InvulnerabilitySeconds==5,"long protection shortened");
    record.Players[0].Bombs=1;player.InvulnerabilitySeconds=0;Call("UseBomb",actor,battle,player);
    Require(player.InvulnerabilitySeconds==3,"missing bomb protection");Call("DamagePlayer",actor,battle,player,20);
    Require(player.CurrentHealth==100,"protected player hurt");
    player.InvulnerabilitySeconds=0;player.InTreasureRoom=true;record.Players[0].Bombs=1;Call("UseBomb",actor,battle,player);
    Require(player.InvulnerabilitySeconds==0&&record.Players[0].Bombs==1,"room bomb was accepted");
});
Check("Dash uses swept constrained island path and preserves off-path and boss targets",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};var player=battle.Players[1];
    player.PositionX=0;player.PositionZ=0;player.MoveX=1;player.LastInputAt=TimeInfo.Instance.ServerNow();record.Players[0].Dashes=1;
    battle.Enemies.Add(1,new OnlineArcadeEnemyRuntime{RuntimeId=1,PositionX=1,PositionZ=0});
    battle.Enemies.Add(2,new OnlineArcadeEnemyRuntime{RuntimeId=2,PositionX=1,PositionZ=4});
    battle.Enemies.Add(3,new OnlineArcadeEnemyRuntime{RuntimeId=3,PositionX=1,PositionZ=0,BossId="boss"});
    Call("UseDash",actor,player);Call("TickPlayers",actor,battle,.2f,player.LastInputAt);
    Require(!battle.Enemies.ContainsKey(1)&&battle.Enemies.ContainsKey(2)&&battle.Enemies.ContainsKey(3),"wrong dash targets");
    Require(record.Players[0].Score==100&&record.Players[1].Score==0,"dash score owner wrong");
    Require(new ArcadeIslandRegion(config.ArcadeRule.ArenaHalfWidth,config.ArcadeRule.ArenaHalfHeight).Contains(player.PositionX,player.PositionZ),"dash outside island");
});
Check("Final clear immediately opens branch exits",()=>{
    var record=Record();var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
    var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};battle.WaveIndex=3;battle.SpawnQueueIndex=battle.SpawnQueue.Count;
    Call("TryAdvanceWave",actor,battle,0f);Require(battle.AwaitingExit&&!battle.ArenaCompletionReady,"wrong clear state");
});
Check("Melee has no drop or shop source",()=>{
    var meleeIds=config.Tables.TbArcadeWeapon.DataList.Where(w=>w.Behavior==ArcadeWeaponBehavior.Melee)
        .Select(w=>w.Id).ToHashSet();
    Require(!config.Tables.TbArcadeDrop.DataList.Any(d=>meleeIds.Contains(d.ProductId)),"melee drop remains");
    Require(typeof(OnlineArcadePlayerRecord).GetProperty("ShopOffers") == null,"trade records remain");
});
Check("Legacy melee attacks use the baseline gun",()=>{
    foreach(var weapon in config.Tables.TbArcadeWeapon.DataList.Where(w=>w.Behavior==ArcadeWeaponBehavior.Melee)){
        var record=Record();record.Players[0].WeaponId=weapon.Id;
        var battle=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
        var actor=new OnlineArcadeRunActor{Record=record,Battle=battle};var player=battle.Players[1];player.AimX=1;
        Require((bool)Call("Fire",actor,battle,player,record.Players[0]),"legacy attack failed");
        Require(battle.Projectiles.Count==1&&battle.Projectiles.Values.Single().Damage==config.ArcadeRule.BaseWeaponId_Ref.Damage,
            "legacy melee did not become baseline projectile");
    }
});
Console.WriteLine($"Opening server checks: {passed}/13 passed.");World.Instance.Dispose();
