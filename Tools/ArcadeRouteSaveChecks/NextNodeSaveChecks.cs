using Game;

/// <summary>用真实客户端文件仓库保存新增三关的12个轮前节点，覆盖原22地图边界。</summary>
internal static class NextNodeSaveChecks
{
    public static int Run()
    {
        int failed=0;
        foreach(int map in new[]{23,24,25})try{Check(map);Console.WriteLine($"PASS NextNodeSave.Map{map}");}
            catch(Exception ex){failed++;Console.WriteLine($"FAIL NextNodeSave.Map{map}: {ex.Message}");}
        Console.WriteLine($"{3-failed}/3 passed");return failed==0?0:1;
    }
    private static void Check(int map)
    {
        string folder=Path.GetFullPath(Path.Combine("Tools/ArcadeRouteSaveChecks/.test-output",Guid.NewGuid().ToString("N")));
        try
        {
            var store=new ArcadeRunFileStore(folder);int order=map-19;
            for(int round=1;round<=4;round++)
            {
                string id=$"arena.{map}.round.{round}";
                var data=new ArcadeRunSaveData{RunId=new string('b',32),IsCasualRoute=true,CasualRouteVersion=1,ArenaConfigNumber=map,
                    CompletedArenaCount=order-1,NextArenaNumber=order,RemainingLives=3,Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=order},UpdatedAtUtc=DateTime.UtcNow.ToString("O"),
                    RoundCheckpoint=new ArcadeRoundCheckpoint{ArenaNumber=order,ArenaConfigNumber=map,RoundNumber=round,MapId=id,LayoutVersion=1,
                        PlayerState=new ArcadeIslandRoundCheckpoint{RoundNumber=round,MapId=id,PlayerHealth=100,PlayerMaxHealth=100,Bombs=1,Dashes=1}}};
                data.EquipmentBySlot["weapon"]="arcade.weapon.basic";
                if(!store.Save(data,out var error))throw new InvalidOperationException(error);
                var read=store.Inspect();
                if(!read.IsValid||read.Data.RoundCheckpoint?.MapId!=id||read.Data.CasualRouteVersion!=1)throw new InvalidOperationException("轮前节点未完整往返");
            }
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
