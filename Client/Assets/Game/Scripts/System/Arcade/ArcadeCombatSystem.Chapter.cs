using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>单人第一章的轮内任务。轮前节点恢复时重新建立本轮，敌人沿用街机战斗和生成预算。</summary>
    internal sealed class ArcadeChapterObjective
    {
        private readonly MissionConfiguration rules;
        private readonly int round;
        private readonly Vector2[] sites;
        private readonly Queue<string> reserves;
        private readonly int total;
        private readonly bool[] searched;
        private int released;
        private float activation;
        private float defense;
        internal int Count { get; private set; }
        internal int Wave { get; private set; }
        internal bool InRange { get; private set; }

        internal ArcadeChapterObjective(int index, MissionConfiguration rules, Vector2[] sites, Queue<string> queue)
        {
            this.rules = rules;
            round = index;
            this.sites = sites;
            searched = new bool[sites.Length];
            reserves = new Queue<string>(queue);
            total = reserves.Count;
            activation = rules.ActivationSeconds;
            defense = rules.DefendSeconds;
            if (index > 0) queue.Clear();
        }

        /// <summary>只在活动战斗步进时调用。每批实际清场后才能开始下一次遭遇。</summary>
        internal void Advance(Vector2 player, float dt, Queue<string> enemies, int alive)
        {
            bool clear = enemies.Count == 0 && alive == 0;
            InRange = Vector2.Distance(player, sites[0]) <= rules.FacilityRadius;
            if (round == 1 && clear)
            {
                int i = Nearest(player);
                if (i >= 0 && Vector2.Distance(player, sites[i]) <= rules.InteractionRadius)
                {
                    searched[i] = true;
                    Release(++Count, sites.Length, enemies);
                }
            }
            if (round == 2 && InRange)
            {
                if (activation > 0)
                {
                    activation = Mathf.Max(0, activation - dt);
                    if (activation == 0) Release(1, 1, enemies);
                }
                else defense = Mathf.Max(0, defense - dt);
            }
            if (round == 3 && clear && Wave < rules.BreakthroughWaves &&
                (Wave > 0 || Vector2.Distance(player, sites[0]) <= rules.InteractionRadius))
                Release(++Wave, rules.BreakthroughWaves, enemies);
        }

        /// <summary>按累计比例切分当前轮的原敌群，同一敌人只进入正式队列一次。</summary>
        private void Release(int part, int parts, Queue<string> queue)
        {
            int target = total * part / parts;
            while (released < target) { queue.Enqueue(reserves.Dequeue()); released++; }
        }

        private int Nearest(Vector2 player)
        {
            int best = -1;
            for (int i = 0; i < searched.Length; i++)
                if (!searched[i] && (best < 0 || (sites[i]-player).sqrMagnitude < (sites[best]-player).sqrMagnitude)) best = i;
            return best;
        }

        internal bool Ready => round == 0 || round == 1 && Count == sites.Length ||
            round == 2 && defense <= 0 || round == 3 && Wave == rules.BreakthroughWaves;

        /// <summary>向现有文字栏提供目标、方向与距离，交互判定只在权威步进中执行。</summary>
        internal string Hint(Vector2 player, bool fighting)
        {
            if (round == 0) return Loc.T("ui.mission.entry");
            if (round == 1)
            {
                int i = Nearest(player);
                return Count + "/" + sites.Length + " · " + (fighting || i < 0 ? Loc.T("ui.mission.clear") :
                    Destination("ui.mission.search", sites[i]-player));
            }
            if (round == 2)
            {
                if (!InRange) return Destination("ui.mission.facility", sites[0]-player);
                if (activation > 0) return Loc.T("ui.mission.activate", Mathf.CeilToInt(activation));
                return defense > 0 ? Loc.T("ui.mission.defend", Mathf.CeilToInt(defense)) : Loc.T("ui.mission.clear");
            }
            return Wave == 0 ? Destination("ui.mission.breakthrough", sites[0]-player) : Loc.T("ui.mission.wave", Wave);
        }

        private static string Destination(string key, Vector2 offset)
        {
            string direction = Mathf.Abs(offset.x) > Mathf.Abs(offset.y) ? (offset.x > 0 ? "east":"west") : (offset.y > 0 ? "north":"south");
            return Loc.T(key, Loc.T("ui.mission." + direction), Mathf.CeilToInt(offset.magnitude));
        }
    }

    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>搜索点和守区沿用本轮已经校验的可达补给点；突破目标取其中最北的位置。</summary>
        private void BeginChapterObjective(ArcadeCombatRuntime runtime)
        {
            if (runtime.ChapterRules == null) return;
            var points = RoundLayout(runtime).WeaponSpawnPoints.Select(p => new Vector2(p.X,p.Y)).ToArray();
            var sites = runtime.WaveIndex == 1 ? points.Take(runtime.ChapterRules.SupplyCount).ToArray() :
                new[] { runtime.WaveIndex == 3 ? points.OrderByDescending(p => p.y).First() : points.OrderBy(p => p.sqrMagnitude).First() };
            runtime.Chapter = new ArcadeChapterObjective(runtime.WaveIndex, runtime.ChapterRules, sites, runtime.SpawnQueue);
        }

        internal string ChapterHint()
        {
            var r = this.GetModel<ArcadeCombatModel>().Runtime;
            if (r?.Chapter == null || r.WaveClearAwarded || r.IslandTransitionPending || r.InTreasureRoom) return null;
            return r.Chapter.Hint(r.PlayerPosition, r.Enemies.Count + r.SpawnQueue.Count > 0);
        }
    }
}
