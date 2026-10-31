using System;
using System.IO;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>独立金币文件的真实读写与重复领取边界。</summary>
    public sealed class SurvivorGoldStoreTests
    {
        [Test] public void UnwritableDirectoryDoesNotReportSuccessfulGold()
        {
            var root=Path.Combine(Path.GetTempPath(),"SurvivorGold-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{File.WriteAllText(Path.Combine(root,"Saves"),"occupied");var store=new SurvivorGoldFileStore(1,root);Assert.That(store.TryBeginRun("run",out var balance,out var error),Is.False);Assert.That(balance,Is.Zero);Assert.That(error,Is.Not.Empty);Assert.That(File.ReadAllText(Path.Combine(root,"Saves")),Is.EqualTo("occupied"));}
            finally{Directory.Delete(root,true);}
        }
        [Test] public void BalanceOverflowDoesNotOverwriteLedger()
        {
            var root=Path.Combine(Path.GetTempPath(),"SurvivorGold-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Saves"));
            try{var path=Path.Combine(root,"Saves","survivor-gold-1.json");var json="{\"Version\":1,\"Balance\":9223372036854775807,\"ActiveRun\":\"run\",\"Sequence\":0}";File.WriteAllText(path,json);var store=new SurvivorGoldFileStore(1,root);Assert.That(store.TryCollect("run:1",out var balance,out var error),Is.False);Assert.That(balance,Is.EqualTo(long.MaxValue));Assert.That(File.ReadAllText(path),Is.EqualTo(json));}
            finally{Directory.Delete(root,true);}
        }
        [Test] public void PickupPersistsExactlyOnceAcrossReopen()
        {
            var type=typeof(SurvivorResultFileStore).Assembly.GetType("Game.SurvivorGoldFileStore");
            Assert.That(type,Is.Not.Null,"缺少独立金币存档");
            var root=Path.Combine(Path.GetTempPath(),"SurvivorGold-"+Guid.NewGuid().ToString("N"));
            try {
                var store=Activator.CreateInstance(type,new object[]{1L,root});
                Assert.That(type.GetMethod("TryBeginRun"),Is.Not.Null,"缺少有界活动局事务");
                Assert.That(type.GetMethod("TryBeginRun").Invoke(store,new object[]{"run-a",0L,null}),Is.True);
                var args=new object[]{"run-a:1",0L,null};
                Assert.That(type.GetMethod("TryCollect").Invoke(store,args),Is.True,args[2]?.ToString());
                Assert.That(args[1],Is.EqualTo(1L));
                store=Activator.CreateInstance(type,new object[]{1L,root});
                args=new object[]{"run-a:1",0L,null};
                Assert.That(type.GetMethod("TryCollect").Invoke(store,args),Is.True);
                Assert.That(args[1],Is.EqualTo(1L));
                args=new object[]{"run-a:2",0L,null};
                Assert.That(type.GetMethod("TryCollect").Invoke(store,args),Is.True);
                Assert.That(args[1],Is.EqualTo(2L));
                Assert.That(type.GetMethod("TryBeginRun").Invoke(store,new object[]{"run-b",0L,null}),Is.True);
                Assert.That(type.GetMethod("TryCollect").Invoke(store,new object[]{"run-a:3",0L,null}),Is.False,"旧局不能写入新局");
                args=new object[]{"run-b:1",0L,null};
                Assert.That(type.GetMethod("TryCollect").Invoke(store,args),Is.True);
                Assert.That(args[1],Is.EqualTo(3L));
                var path=Path.Combine(root,"Saves","survivor-gold-1.json");
                File.WriteAllText(path,"broken");
                Assert.That(type.GetMethod("TryCollect").Invoke(store,new object[]{"run-b:2",0L,null}),Is.False);
                Assert.That(File.ReadAllText(path),Is.EqualTo("broken"));
            } finally { if(Directory.Exists(root))Directory.Delete(root,true); }
        }
    }
}
