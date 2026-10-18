using System.Reflection;
using ET;
using ET.Server;
using MemoryPack;

// 范围：正式服务端死亡表现事实，仅6项；不启动监听器、不读取或写入玩家存档。
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var config = ServerConfig.CreateForValidation("OnlineLocal", name => File.ReadAllBytes(Path.Combine(root, "Server/GameServer/Config/Luban", name + ".bytes")));
World.Instance.AddSingleton(config); World.Instance.AddSingleton<TimeInfo>();
World.Instance.AddSingleton<ObjectPool>();
object Call(string method, params object[] values) => typeof(OnlineArcadeRunActorSystem)
    .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values)!;
void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
OnlineArcadeRunActor Actor(int arena = 1)
{
    var record = new OnlineArcadeRunRecord { RunId = 17, RoomId = 1, Revision = 1, NextArena = arena, ArenaConfigNumber = arena,
        ConfigFingerprint = config.ArcadeRule.ConfigFingerprint, IsCasualRoute = true, Phase = OnlineArcadeRunPhase.SafeCheckpoint,
        Players = new() {
            new() { PlayerId = 1, Lives = 3, Bombs = 1, Dashes = 1, Ready = true, WeaponId = config.ArcadeRule.BaseWeaponId },
            new() { PlayerId = 2, Lives = 3, Bombs = 1, Dashes = 1, Ready = true, WeaponId = config.ArcadeRule.BaseWeaponId } } };
    var battle = OnlineArcadeRunActorSystem.PrepareBattle(record, false); battle.RoundGeneration = 7;
    battle.Players[1].PositionX = 0; battle.Players[1].PositionZ = 0;
    return new OnlineArcadeRunActor { Record = record, Battle = battle };
}
OnlineArcadeEnemyRuntime Enemy(long id, float x, float z) => new() { RuntimeId = id, CurrentHealth = 1, MaxHealth = 20,
    PositionX = x, PositionZ = z, LastDamagePlayerId = 1 };

try
{
    var actor = Actor(); var battle = actor.Battle;
    var enemy = Enemy(1, 3, 0); battle.Enemies.Add(1, enemy);
    Call("UseBomb", actor, battle, battle.Players[1]);
    Require(battle.Enemies.Count == 0 && battle.EnemyDeaths.Count == 1 && battle.EnemyDeaths[0].Explosive, "bomb death metadata missing");
    Require(Math.Abs(battle.EnemyDeaths[0].DirectionX - 1) < .001f && battle.EnemyDeaths[0].RoundGeneration == 7, "bomb direction or generation wrong");
    Call("KillEnemy", actor, battle, enemy, true);
    Require(battle.EnemyDeaths.Count == 1, "duplicate kill replayed corpse");
    Console.WriteLine("PASS Bomb death direction, category and duplicate removal");

    actor = Actor(); battle = actor.Battle;
    enemy = Enemy(2, 3, 2); battle.Enemies.Add(2, enemy); battle.EnemyIterationIds.Add(2);
    Call("DamageEnemiesInExplosionRadius", actor, battle, 1f, 2f, 4f, 20, 1L);
    Require(battle.EnemyDeaths.Count == 1 && battle.EnemyDeaths[0].Explosive && battle.EnemyDeaths[0].DirectionX > .99f, "rocket explosion source lost");
    for (long id = 3; id <= 32; id++) { var value = Enemy(id, 3, 0); battle.Enemies.Add(id, value); Call("KillEnemy", actor, battle, value, true); }
    Require(battle.EnemyDeaths.Count == 24 && battle.EnemyDeaths[0].RuntimeId == 9 && !battle.EnemyDeaths[^1].Explosive, "death receipt budget or ordinary category wrong");
    Console.WriteLine("PASS Rocket source and bounded ordinary death receipts");

    var snapshot = (OnlineArcadeBattleInfo)Call("BuildBattleSnapshot", battle);
    var clone = (OnlineArcadeBattleInfo)Call("CloneBattleSnapshot", snapshot);
    var decoded = MemoryPackSerializer.Deserialize<OnlineArcadeBattleInfo>(MemoryPackSerializer.Serialize(snapshot))!;
    Require(decoded.EnemyDeaths.Count == 24 && decoded.EnemyDeaths[0].RuntimeId == 9 && decoded.EnemyDeaths[0].RoundGeneration == 7, "protocol round trip dropped deaths");
    snapshot.EnemyDeaths[0].DirectionX = 99;
    Require(clone.EnemyDeaths[0].DirectionX < 2, "snapshot clone shares mutable death records");
    Call("PrepareWave", battle, config.Tables.TbArcadeArena.Get(1), 1);
    Require(battle.EnemyDeaths.Count == 0, "new round kept old death records");
    Console.WriteLine("PASS Protocol serialization, deep clone and round isolation");
    actor = Actor(); battle = actor.Battle;
    battle.Enemies.Add(1, Enemy(1, 3, 0)); battle.EnemyIterationIds.Add(1);
    Call("DamageEnemiesInRadius", actor, battle, 0f, 0f, 4f, 20, 1L);
    Require(battle.EnemyDeaths.Count == 1 && !battle.EnemyDeaths[0].Explosive, "electric/saw circular damage became explosion");
    Console.WriteLine("PASS Electric and saw damage remain ordinary death feedback");

    actor = Actor(3); battle = actor.Battle;
    var barrel = battle.RoundLayout.Barrels[0];
    // 目标留在爆炸半径内、弹丸接触半径外，避免测试起点被近敌遮挡。
    battle.Enemies.Add(1, Enemy(1, barrel.X + 2, barrel.Y));
    var shot = new OnlineArcadeProjectileRuntime
    {
        OwnerPlayerId = 1, PositionX = barrel.X, PositionZ = barrel.Y,
        FromPlayerWeapon = true, RemainingSeconds = 1
    };
    Require((bool)Call("TryHitGroundBarrel", actor, battle, shot, barrel.X, barrel.Y, barrel.X, barrel.Y), "ground barrel did not explode");
    Require(battle.EnemyDeaths.Count == 1 && battle.EnemyDeaths[0].Explosive && battle.EnemyDeaths[0].DirectionX > .99f, "ground barrel lost explosion source");
    Console.WriteLine("PASS Ground barrel death uses authoritative explosion center");

    actor = Actor(3); battle = actor.Battle;
    var carrier = Enemy(1, 0, 0); carrier.SpecialKind = 1;
    battle.Enemies.Add(1, carrier); battle.Enemies.Add(2, Enemy(2, 1, 0));
    Call("KillEnemy", actor, battle, carrier, false);
    Require(battle.EnemyDeaths.Count == 2 && battle.EnemyDeaths.Single(death => death.RuntimeId == 2).Explosive, "carrier chain lost explosion category");
    Console.WriteLine("PASS Explosive carrier chain marks nearby victim");
    Console.WriteLine("Enemy physical feedback server checks: 6/6 passed.");
}
finally { World.Instance.Dispose(); }
