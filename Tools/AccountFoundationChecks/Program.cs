using FirepowerLoop.Accounts;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length == 2 && args[0] == "--drop-test-database")
{
    if (!args[1].StartsWith("firepowerloop_tests_native_", StringComparison.Ordinal) || args[1].Length != "firepowerloop_tests_native_".Length + 32) throw new Exception("不是隔离夹具数据库");
    var cleanupClient = new MongoClient(Environment.GetEnvironmentVariable("FIREPOWERLOOP_TEST_MONGODB_URI") ?? throw new Exception("缺少隔离Mongo配置"));
    await cleanupClient.DropDatabaseAsync(args[1]); Console.WriteLine("隔离数据库已清理"); return;
}
int passed=0;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
Check(AccountPolicy.UserName(new string('a',32))&&!AccountPolicy.UserName(new string('a',33))&&!AccountPolicy.UserName("中文账号"),"用户名共同边界3–32 ASCII");
Check(AccountPolicy.NewPassword("Password-123")&&!AccountPolicy.NewPassword("12345678")&&!AccountPolicy.NewPassword(new string('中',100)),"新密码共同字符及UTF8边界");
var clock = new ManualClock(); var guard=new AccountFailureGuard(clock);for(int i=0;i<4;i++)Check(!guard.Failure("test"),"失败阈值前允许重试");Check(guard.Failure("test")&&guard.IsLocked("test"),"第五次失败锁定账号");clock.Advance(TimeSpan.FromMinutes(10)); Check(!guard.IsLocked("test")&&!guard.Failure("test"),"锁定到期可重试且旧失败窗口清空"); guard.Success("test");Check(!guard.IsLocked("test"),"成功清理失败状态");
var requests=new AccountRequestGuard(clock);for(int i=0;i<120;i++)if(!requests.Allow(System.Net.IPAddress.Loopback))throw new Exception("提前限流");Check(!requests.Allow(System.Net.IPAddress.Loopback),"每IP每分钟120次认证上限");
clock.Advance(TimeSpan.FromMinutes(1));Check(requests.Allow(System.Net.IPAddress.Loopback),"IP限流窗口到期恢复");
string uri=Environment.GetEnvironmentVariable("FIREPOWERLOOP_TEST_MONGODB_URI")??throw new Exception("缺少隔离测试Mongo配置");
var client=new MongoClient(uri);string databaseName="firepowerloop_tests_accounts_"+Guid.NewGuid().ToString("N");var database=client.GetDatabase(databaseName);
using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));var token=timeout.Token;
try
{
 var store=new MongoIdentityStore(database);await store.InitializeAsync(token);var auth=new AccountAuthenticationService(store);
 Check((await auth.LoginAsync("new_player","Password-123",token)).Error==200104&&await store.FindAsync("new_player",token)==null,"登录不存在账号不会自动建号");
 Check((await auth.RegisterAsync("new_player","12345678",token)).Error==200102,"新注册拒绝旧短密码");
 var raced=await Task.WhenAll(auth.RegisterAsync("new_player","Password-123",token),auth.RegisterAsync("NEW_PLAYER","Password-123",token));
 Check(raced.Count(r=>r.Error==0)==1&&raced.Count(r=>r.Error==200103)==1,"并发同名注册只有一个身份");
 var login=await auth.LoginAsync("new_player","Password-123",token);Check(login.Error==0&&login.PlayerId>0&&login.SessionEpoch>0,"登录原子建立玩家映射和持久世代");
 var reopened=new AccountAuthenticationService(new MongoIdentityStore(client.GetDatabase(databaseName)));var again=await reopened.LoginAsync("NEW_PLAYER","Password-123",token);
 Check(again.AccountId==login.AccountId&&again.PlayerId==login.PlayerId&&again.SessionEpoch>login.SessionEpoch,"重开仓储保留ID且世代继续递增");
 Check(await auth.ValidateSessionAsync(login.AccountId,login.SessionEpoch,token)==200105&&await auth.ValidateSessionAsync(again.AccountId,again.SessionEpoch,token)==0,"旧世代失效且新世代有效");
 var salt=RandomNumberGenerator.GetBytes(16);var hash=Rfc2898DeriveBytes.Pbkdf2("old123",salt,600000,HashAlgorithmName.SHA256,32);string now=DateTime.UtcNow.ToString("O");
 string json=JsonSerializer.Serialize(new{SchemaVersion=4,NextAccountId=1100001L,NextPlayerId=2100001L,
 Accounts=new[]{new{AccountId=1100000L,NormalizedUserName="legacy_user",DisplayUserName="Legacy_User",AlgorithmVersion=1,Iterations=600000,SaltBase64=Convert.ToBase64String(salt),HashBase64=Convert.ToBase64String(hash),PlayerId=2100000L,CreatedAtUtc=now}},
 Players=new[]{new{PlayerId=2100000L,AccountId=1100000L,DisplayName="Legacy_User",Revision=3L,CreatedAtUtc=now,UpdatedAtUtc=now}}});
 var conflictingPlayer = LegacyIdentityImport.Parse(json.Replace("2100000",login.PlayerId.ToString()));
 try { await conflictingPlayer.ApplyAsync(store,token); throw new Exception("允许不同账号占用已有玩家"); } catch(InvalidDataException) { }
 Check(await store.FindByIdAsync(1100000,token)==null&&(await store.FindByIdAsync(login.AccountId,token))!.PlayerId==login.PlayerId,
     "跨账号玩家编号冲突拒绝迁入且原映射保持");
 Check((await auth.RegisterAsync("unmapped_two","Password-123",token)).Error==0&&(await auth.RegisterAsync("unmapped_three","Password-123",token)).Error==0,
     "玩家唯一索引允许多个尚无玩家映射的账号");
 var import=LegacyIdentityImport.Parse(json);await import.ApplyAsync(store,token);await import.ApplyAsync(store,token);
 var conflict = LegacyIdentityImport.Parse(json.Replace("Legacy_User", "LEGACY_User"));
 try { await conflict.ApplyAsync(store,token); throw new Exception("冲突导入未拒绝"); } catch(InvalidDataException) { }
 Check((await store.FindByIdAsync(1100000,token))!.DisplayUserName=="Legacy_User","迁移冲突停止且不覆盖原身份");
 var old=await auth.LoginAsync("legacy_user","old123",token);Check(old.Error==0&&old.AccountId==1100000&&old.PlayerId==2100000&&old.ProfileRevision==3&&old.NeedsPasswordUpgrade,"显式迁入当前项目旧身份可重跑，保留ID资料及旧密码登录");
 Check(await store.NextIdAsync("account",token)>1100000&&await store.NextIdAsync("player",token)>2100000,"迁移预留旧编号水位不回收");
 try{LegacyIdentityImport.Parse(json.Replace("\"SchemaVersion\":4","\"SchemaVersion\":3"));throw new Exception("允许旧业务schema");}catch(InvalidDataException){}
 Check(true,"拒绝非目标schema4数据");
 await database.GetCollection<IdentityAccount>("accounts").UpdateOneAsync(a=>a.AccountId==login.AccountId,Builders<IdentityAccount>.Update.Set(a=>a.Status,"banned"),cancellationToken:token);
 Check((await auth.LoginAsync("new_player","Password-123",token)).Error==200204,"封禁状态拒绝登录");
 Check(await auth.ValidateSessionAsync(again.AccountId,again.SessionEpoch,token)==200204,"已登录身份封禁后心跳校验拒绝");
 Console.WriteLine($"账号基础检查通过：{passed}项，包含真实隔离Mongo；未运行玩家实际数据迁移。");
}
finally{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(10));await client.DropDatabaseAsync(databaseName,cleanup.Token);}

internal sealed class ManualClock : TimeProvider
{
 private DateTimeOffset now = DateTimeOffset.UtcNow;
 public override DateTimeOffset GetUtcNow() => now;
 internal void Advance(TimeSpan duration) => now += duration;
}
