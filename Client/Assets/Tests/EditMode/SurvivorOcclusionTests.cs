using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>限定验证遮挡优化与旧采样逐输入一致，以及无关障碍的检查开销。</summary>
    public sealed class SurvivorOcclusionTests
    {
        private ArcadeObstacleDefinition[] obstacles;
        private Func<Vector2, Vector2, bool> blocked;
        private void Bind(ArcadeObstacleDefinition[] source)
        {
            obstacles = source;
            var system = new SurvivorSystem();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SurvivorSystem).GetField("obstacles", flags).SetValue(system, source);
            blocked = (Func<Vector2, Vector2, bool>)Delegate.CreateDelegate(typeof(Func<Vector2, Vector2, bool>),
                system, typeof(SurvivorSystem).GetMethod("Blocked", flags));
        }
        /// <summary>原实现作为运行时基线，保留Mono的原始浮点求值顺序。</summary>
        private bool Baseline(Vector2 from, Vector2 to)
        {
            int steps = Math.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / .1f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(from, to, (float)i / steps);
                foreach (var o in obstacles)
                    if (o.BlocksProjectiles && Mathf.Abs(p.x - o.Center.x) <= o.HalfExtents.x &&
                        Mathf.Abs(p.y - o.Center.y) <= o.HalfExtents.y) return true;
            }
            return false;
        }
        [Test]
        public void FormalFarmMatchesOriginalSampling()
        {
            var tables = new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes"))));
            var cfg = SurvivorConfiguration.FromTables(tables);
            Bind(cfg.Layout.Obstacles.Select(o => new ArcadeObstacleDefinition(o.X, o.Y, o.HalfWidth, o.HalfHeight, o.BlocksProjectiles)).ToArray());
            var random = new System.Random(20261101);
            for (int i = 0; i < 20000; i++)
            {
                var a = new Vector2((float)random.NextDouble()*61-30.5f, (float)random.NextDouble()*61-30.5f);
                var b = new Vector2((float)random.NextDouble()*61-30.5f, (float)random.NextDouble()*61-30.5f);
                Assert.That(blocked(a,b), Is.EqualTo(Baseline(a,b)), "正式场地线段 " + i);
            }
        }
        [Test]
        public void BoundaryShortAndReversedSegmentsMatchOriginal()
        {
            foreach (float center in new[] {0f, 3.123456f, -30.5f, 100000f})
            {
                Bind(new[] {new ArcadeObstacleDefinition(center, center, .37f, .23f, true)});
                foreach(float offset in new[] {-.370001f, -.37f, -.369999f, 0f, .229999f, .23f, .230001f, .37f, .370001f})
                foreach(float length in new[] {0f, .000001f, .099999f, .1f, .100001f, 2f, 17f})
                {
                    var a = new Vector2(center - length, center + offset);
                    var b = new Vector2(center + length, center + offset);
                    Assert.That(blocked(a,b), Is.EqualTo(Baseline(a,b)));
                    Assert.That(blocked(b,a), Is.EqualTo(Baseline(b,a)));
                    a = new Vector2(center+offset,center-length); b = new Vector2(center+offset,center+length);
                    Assert.That(blocked(a,b), Is.EqualTo(Baseline(a,b)));
                    Assert.That(blocked(b,a), Is.EqualTo(Baseline(b,a)));
                }
            }
        }
        [Test]
        public void EmptyNonblockingAndSampleGapKeepOriginalRules()
        {
            Bind(Array.Empty<ArcadeObstacleDefinition>());
            Assert.That(blocked(Vector2.zero, Vector2.one), Is.False);
            Bind(new[] {new ArcadeObstacleDefinition(0,0,1,1,false)});
            Assert.That(blocked(Vector2.zero, Vector2.one), Is.False);
            Bind(new[] {new ArcadeObstacleDefinition(.05f,0,.001f,.001f,true)});
            Assert.That(blocked(Vector2.zero,new Vector2(.1f,0)), Is.False, "不改成连续求交而改变原采样间隙");
            Assert.That(blocked(new Vector2(.05f,0),new Vector2(.05f,0)), Is.True);
            Assert.That(blocked(Vector2.zero,new Vector2(.05f,0)), Is.True, "包含端点");
        }
        [Test]
        public void IrrelevantObstaclesCostLessThanRepeatedSampling()
        {
            Bind(Enumerable.Range(0,15).Select(i => new ArcadeObstacleDefinition(i,20,1,1,true)).ToArray());
            var from = new Vector2(-10,-20); var to = new Vector2(10,-20);
            Action original = () => {for(int i=0;i<80;i++) Baseline(from,to);};
            Action optimized = () => {for(int i=0;i<80;i++) blocked(from,to);};
            for(int i=0;i<8;i++){original();optimized();}
            var oldTimes = new double[15]; var newTimes = new double[15];
            for(int i=0;i<15;i++)
            {
                if(i%2==0){oldTimes[i]=Measure(original);newTimes[i]=Measure(optimized);}
                else {newTimes[i]=Measure(optimized);oldTimes[i]=Measure(original);}
            }
            Array.Sort(oldTimes);Array.Sort(newTimes);
            Assert.That(newTimes[7], Is.LessThan(oldTimes[7]*.5), "无关障碍不应仍沿整条射线重复检查");
        }
        private static double Measure(Action action)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();action();watch.Stop();return watch.Elapsed.TotalMilliseconds;
        }
    }
}
