// 独立检查只替代Unity/QFramework和配置接入，事务/模型/文件代码均编译正式源。
namespace QFramework
{
    public abstract class AbstractModel { protected abstract void OnInit(); }
    public abstract class AbstractSystem
    {
        public Game.ArcadeRunModel Model;
        protected abstract void OnInit();
        public T GetModel<T>() where T : class => Model as T;
        public T GetUtility<T>() where T : class => new Game.CheckConfigProvider() as T;
        public void SendEvent<T>(T value) { }
    }
}
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup => 0; } }
namespace FirepowerLoop.Arcade
{
    public readonly struct ArcadeTreasureScoreState
    {
        public ArcadeTreasureScoreState(long score,int multiplier,int progress,int lives) { Score=score;Multiplier=multiplier;Progress=progress;Lives=lives; }
        public long Score { get; } public int Multiplier { get; } public int Progress { get; } public int Lives { get; }
    }
}
namespace Game
{
    public static class Log { public static void Error(string value) => throw new System.Exception(value); }
    public interface IRuntimeConfigProvider { bool TryGetArcadeCatalog(out CheckCatalog value,out string error); }
    public sealed class CheckConfigProvider : IRuntimeConfigProvider
    {
        public bool TryGetArcadeCatalog(out CheckCatalog value,out string error) { value=new CheckCatalog();error=null;return true; }
    }
    public sealed class CheckCatalog
    {
        public CheckRule Rule = new CheckRule();
        public CheckArena[] Arenas = new[] { new CheckArena { ArenaNumber=1,NextArenaNumbers=new List<int>{2} },new CheckArena { ArenaNumber=2,NextArenaNumbers=new List<int>{3} },new CheckArena { ArenaNumber=3 } };
        public CheckChallenge[] Challenges = System.Array.Empty<CheckChallenge>();
    }
    public sealed class CheckRule { public int InitialLives=3,InitialBombs=1,InitialDashes=1;public string BaseWeaponId="arcade.weapon.basic"; }
    public sealed class CheckArena { public int ArenaNumber;public List<int> NextArenaNumbers=new List<int>();public string FinalBossId; }
    public sealed class CheckChallenge { public string Id; }
}
