using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class TestToolSettingsModel : AbstractModel
    {
        public const int MaxRecentCommandCount = 32;

        private readonly List<string> recentCommands = new List<string>();
        public BindableProperty<Vector2> TestCmdButtonNormalizedPosition { get; } =
            new BindableProperty<Vector2>(new Vector2(
                TestToolSettingsData.DefaultTestCmdButtonPositionX,
                TestToolSettingsData.DefaultTestCmdButtonPositionY));

        public IReadOnlyList<string> RecentCommands => recentCommands;

        protected override void OnInit()
        {
        }

        public void Apply(TestToolSettingsData data)
        {
            var sanitized = (data ?? TestToolSettingsData.CreateDefault()).Clone().Sanitize();
            TestCmdButtonNormalizedPosition.Value = new Vector2(
                sanitized.TestCmdButtonPositionX,
                sanitized.TestCmdButtonPositionY);
            recentCommands.Clear();
            recentCommands.AddRange(sanitized.RecentCommands);
        }

        public TestToolSettingsData ToData()
        {
            var position = SanitizePosition(TestCmdButtonNormalizedPosition.Value);
            return new TestToolSettingsData
            {
                TestCmdButtonPositionX = position.x,
                TestCmdButtonPositionY = position.y,
                RecentCommands = recentCommands.ToList()
            }.Sanitize();
        }

        public void SetTestCmdButtonPosition(Vector2 normalizedPosition)
        {
            TestCmdButtonNormalizedPosition.Value = SanitizePosition(normalizedPosition);
        }

        public bool RecordSubmittedCommand(string commandText)
        {
            var normalized = (commandText ?? string.Empty).Trim();
            if (!TestToolSettingsData.IsSubmittedCommand(normalized))
            {
                return false;
            }

            var index = recentCommands.FindIndex(item => string.Equals(item, normalized, StringComparison.Ordinal));
            if (index == 0)
            {
                return false;
            }

            if (index > 0)
            {
                recentCommands.RemoveAt(index);
            }

            recentCommands.Insert(0, normalized);
            if (recentCommands.Count > MaxRecentCommandCount)
            {
                recentCommands.RemoveRange(MaxRecentCommandCount, recentCommands.Count - MaxRecentCommandCount);
            }

            return true;
        }

        private static Vector2 SanitizePosition(Vector2 value)
        {
            return new Vector2(
                SanitizeAxis(value.x, TestToolSettingsData.DefaultTestCmdButtonPositionX),
                SanitizeAxis(value.y, TestToolSettingsData.DefaultTestCmdButtonPositionY));
        }

        private static float SanitizeAxis(float value, float defaultValue)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? defaultValue
                : Mathf.Clamp01(value);
        }
    }
}
