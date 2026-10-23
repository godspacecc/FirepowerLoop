using System.Reflection;
using ET;
using ET.Server;

/// <summary>真实服务器出生入口的半批重叠检查；只创建内存对局，不启动服务器或读取玩家档。</summary>
internal static class PassageOverlapOnlineChecks
{
    private static object Call(string name, params object[] values) => typeof(OnlineArcadeRunActorSystem)
        .GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, values)!;

    public static int Run(int maximum)
    {
        var checks = new (string Name, Action Check)[] {
            ("SecondPassageStartsAtHalfBeforeFirstFinishes", HalfBoundary),
            ("OldAndNewPassagesBothContinue", BothContinue),
            ("EveryMapAndRoundOverlaps", EveryLayout),
            ("CapacityNeverConsumesPendingBudget", Capacity),
            ("WaitingPlayersFreezeEveryMap", Waiting),
            ("AllPassagesFinishExactFiniteBudget", FiniteBudget)
        };
        if (maximum < checks.Length) throw new ArgumentException("PassageOverlapOnline maximum too small");
        int failed = 0;
        foreach (var (name, test) in checks)
            try { test(); Console.WriteLine("PASS PassageOverlapOnline." + name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL PassageOverlapOnline." + name + ": " + (e.InnerException ?? e).Message); }
        Console.WriteLine($"{checks.Length - failed}/{checks.Length} passed");return failed == 0 ? 0 : 1;
    }

    private static OnlineArcadeBattleRuntime Create(int map=1,int round=1)
    {
        var rule=ServerConfig.Instance.ArcadeRule;
        var record=new OnlineArcadeRunRecord{RunId=1,RoomId=2,Revision=1,NextArena=1,
            ArenaConfigNumber=map,RoundNumber=round,IsCasualRoute=true,ConfigFingerprint=rule.ConfigFingerprint,
            Players=new(){new(){PlayerId=1,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId},new(){PlayerId=2,Lives=3,Ready=true,WeaponId=rule.BaseWeaponId}}};
        var b=OnlineArcadeRunActorSystem.PrepareBattle(record,false);
        foreach(var p in b.Players.Values)p.ReadyRoundGeneration=b.RoundGeneration;
        b.NextSpawnAt=0;return b;
    }

    private static int Passage(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e)
    {
        var entries=b.RoundLayout.Entries;
        return Enumerable.Range(0,entries.Count).OrderBy(i=>b.ArenaConfigNumber==1
            ? -(e.PositionX*entries[i].X+e.PositionZ*entries[i].Y)/Math.Sqrt(entries[i].X*entries[i].X+entries[i].Y*entries[i].Y)
            : Math.Pow(e.PositionX-entries[i].X,2)+Math.Pow(e.PositionZ-entries[i].Y,2)).First();
    }
    private static int[] Step(OnlineArcadeBattleRuntime b,int tick)
    {
        b.Enemies.Clear();Call("TrySpawnEnemy",b,10000L+tick*1000L);
        return b.Enemies.Values.Select(e=>Passage(b,e)).ToArray();
    }
    private static int FirstBudget(OnlineArcadeBattleRuntime b)=>(b.TotalInWave+b.RoundLayout.Entries.Count-1)/b.RoundLayout.Entries.Count;
    private static void HalfBoundary()
    {
        var b=Create();int half=(FirstBudget(b)+1)/2;var seen=new HashSet<int>();
        for(int i=0;i<half-1;i++){seen.UnionWith(Step(b,i));Require(seen.Count==1,"Second passage opened before half batch");}
        seen.UnionWith(Step(b,half-1));
        Require(seen.Count==2,"Next passage must start when first batch reaches 50%, before it finishes");
    }
    private static void BothContinue()
    {
        var b=Create();int first=Step(b,0).Single(),half=(FirstBudget(b)+1)/2;
        for(int i=1;i<half;i++)Step(b,i);
        var next=Step(b,half);Require(next.Contains(first)&&next.Distinct().Count()>=2,"Old passage stopped, or new passage still waits for old batch completion");
    }
    private static void EveryLayout()
    {
        foreach(var row in ServerConfig.Instance.Tables.TbArcadeRoundLayout.DataList)
        {
            var b=Create(row.ArenaConfigNumber,row.RoundNumber);var seen=new HashSet<int>();int half=(FirstBudget(b)+1)/2;
            // 传送类敌人会在原有特殊初始化中改写出生位置，通道判定读取权威调度选定值。
            for(int i=0;i<half;i++){Step(b,i);seen.Add(b.IslandPassages.Current);}
            Require(seen.Count>=2,row.Id+" did not overlap passages by half batch");
        }
    }
    private static void Capacity()
    {
        var b=Create();b.MaxAlive=1;Step(b,0);int before=b.SpawnQueueIndex;
        for(int i=1;i<80;i++)Call("TrySpawnEnemy",b,10000L+i*1000L);
        Require(b.SpawnQueueIndex==before&&b.Enemies.Count==1,"Full capacity consumed queue or exceeded cap");
        b.Enemies.Clear();Call("TrySpawnEnemy",b,100000L);Require(b.Enemies.Count==1,"Unblocking capacity caused a spawn burst");
    }
    private static void Waiting()
    {
        foreach(int map in new[]{1,21,22,2,20})foreach(int phase in new[]{0,1,2,3})
        {
            var b=Create(map);
            if(phase==0)b.Players[1].RespawnSeconds=5;
            if(phase==1)b.Players[1].InTreasureRoom=true;
            if(phase==2)b.RoundTransitionPending=true;
            if(phase==3)b.WaveClearAwarded=true;
            Call("TrySpawnEnemy",b,10000L);
            Require(b.SpawnQueueIndex==0&&b.Enemies.Count==0,$"Map {map} waiting phase {phase} still spawned");
        }
    }
    private static void FiniteBudget()
    {
        foreach(int map in new[]{1,21,22,2,20})
        {
            var b=Create(map);int total=b.TotalInWave;var seen=new HashSet<int>();
            for(int i=0;i<total+5;i++)seen.UnionWith(Step(b,i));
            Require(b.SpawnQueueIndex==total&&b.SpawnedInWave==total,"Finite queue lost or duplicated enemies");
            Require(seen.Count==b.RoundLayout.Entries.Count,"Some passages never received their batch");
        }
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
