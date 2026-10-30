using FirepowerLoop.Accounts;
using MongoDB.Driver;

// 默认只验证本项目显式指定快照。应用需要额外参数和本项目专用数据库，不移动、删除源文件。
if (args.Length is < 2 or > 3 || args[0] != "--source" || (args.Length == 3 && args[2] != "--apply"))
{
    Console.Error.WriteLine("用法：--source <当前项目schema4快照路径> [--apply]；应用前停止本项目账号服务并备份。");
    return 2;
}
try
{
    var source = Path.GetFullPath(args[1]);
    var repository = new DirectoryInfo(AppContext.BaseDirectory);
    while (repository != null && !(Directory.Exists(Path.Combine(repository.FullName, "Client")) && File.Exists(Path.Combine(repository.FullName, "AGENTS.md")))) repository = repository.Parent;
    if (repository == null || !source.StartsWith(repository.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("只接受当前FirepowerLoop仓库内显式指定的快照。");
    var import = LegacyIdentityImport.Parse(await File.ReadAllTextAsync(source));
    Console.WriteLine($"快照校验通过：{import.Accounts.Count}个账号；源文件保持不变。");
    if (args.Length == 2) return 0;
    string uri = Environment.GetEnvironmentVariable("FIREPOWERLOOP_MONGODB_URI") ?? throw new InvalidOperationException();
    string database = Environment.GetEnvironmentVariable("FIREPOWERLOOP_MONGODB_DATABASE") ?? "firepowerloop_accounts";
    if (!database.StartsWith("firepowerloop_", StringComparison.Ordinal)) throw new InvalidOperationException();
    var settings = MongoClientSettings.FromConnectionString(uri);
    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(8);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
    var store = new MongoIdentityStore(new MongoClient(settings).GetDatabase(database));
    await store.InitializeAsync(timeout.Token);
    await import.ApplyAsync(store, timeout.Token);
    Console.WriteLine("导入核对完成。请验证旧账号登录和玩家编号后手动归档源文件，再启动服务。未删除或覆盖源文件。");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"迁移停止：{error.GetType().Name}。检查快照格式、数据库配置及冲突记录；不打印凭据。");
    return 1;
}
