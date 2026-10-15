using NUnit.Framework;

namespace Game.Tests
{
    public sealed class FootstepCadenceTests
    {
        private FootstepCadence cadence;

        [SetUp]
        public void SetUp()
        {
            cadence = new FootstepCadence(0.65f, 0.9f, 0.12f, 4.5f);
        }

        private bool Step(float distance, float dt = 0.2f, bool eligible = true, bool grounded = true)
            => cadence.Step(distance, dt, eligible, grounded);

        [Test] public void AccumulatesRealDistance() { Assert.False(Step(0.4f)); Assert.True(Step(0.3f)); }
        [Test] public void WallOrStopClearsRemainder() { Step(0.4f); Step(0f); Assert.False(Step(0.3f)); }
        [Test] public void AirborneMovementDoesNotCount() { Step(0.4f); Step(1f, grounded: false); Assert.False(Step(0.3f)); }
        [Test] public void IneligibleMovementClearsRemainder() { Step(0.4f); Step(1f, eligible: false); Assert.False(Step(0.3f)); }
        [Test] public void InvalidInputClearsRemainder() { Step(0.4f); Step(float.NaN); Assert.False(Step(0.3f)); }
        [Test] public void ResetStartsNewCadence() { Step(0.4f); cadence.Reset(); Assert.False(Step(0.3f)); }
        [Test] public void FastMovementUsesLongerStride() { Assert.False(Step(0.5f, 0.1f)); Assert.True(Step(0.5f, 0.1f)); }
        [Test] public void LongFrameDoesNotQueueExtraSteps() { Assert.True(Step(3f)); Assert.False(Step(0.01f)); }

        // 防止步距与速度同比增长，再次把走路和加速跑压成相同发声频率。
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void ProductionProfileKeepsSprintAudiblyFasterThanWalking(int framesPerSecond)
        {
            var profile = UnityEditor.AssetDatabase.LoadAssetAtPath<FootstepProfile>("Assets/Game/Config/FootstepProfile.asset");
            Assert.NotNull(profile);
            int walk = CountSteps(profile, 5f, framesPerSecond);
            int run = CountSteps(profile, 7.5f, framesPerSecond);
            Assert.That(walk, Is.InRange(24, 26), "十秒走路应保持舒缓节奏");
            Assert.That(run, Is.InRange(34, 36), "十秒加速跑应明显更密集");
            Assert.That(run, Is.GreaterThanOrEqualTo(walk * 1.3f));
        }

        private static int CountSteps(FootstepProfile profile, float speed, int framesPerSecond)
        {
            var counter = new FootstepCadence(profile.WalkStride, profile.RunStride, profile.MinimumInterval, profile.FastSpeed);
            int count = 0;
            float dt = 1f / framesPerSecond;
            for (int i = 0; i < framesPerSecond * 10; i++)
                if (counter.Step(speed * dt, dt, true, true)) count++;
            return count;
        }
    }
}
