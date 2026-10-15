using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Game
{
    public static class MissingCharCollector
    {
        private const string DefaultCharsetPath = "Assets/Game/Fonts/3500hz.txt";
        private const string TargetFontAssetName = "[SourceHanSansCN-Normal SDF]";
        private static readonly Regex MissingUnicodeRegex = new Regex(
            @"Unicode value \\u([0-9A-Fa-f]{4})(?:\\u([0-9A-Fa-f]{4}))? was not found",
            RegexOptions.Compiled);

        private static readonly HashSet<int> ExistingCodePoints = new HashSet<int>();
        private static string _charsetPathOverride;
        private static bool _isInitialized;

        private static string CharsetPath => string.IsNullOrWhiteSpace(_charsetPathOverride)
            ? DefaultCharsetPath
            : _charsetPathOverride;

        public static void Init()
        {
            if (_isInitialized)
            {
                return;
            }

            LoadCharset();

            if (Application.isEditor && Application.isPlaying)
            {
                Application.logMessageReceived += OnLogMessage;
            }

            _isInitialized = true;
        }

        public static void ResetForTests()
        {
            if (_isInitialized && Application.isEditor)
            {
                Application.logMessageReceived -= OnLogMessage;
            }

            _isInitialized = false;
            _charsetPathOverride = null;
            ExistingCodePoints.Clear();
        }

        public static void SetCharsetPathForTests(string charsetPath)
        {
            _charsetPathOverride = charsetPath;
        }

        public static bool TryCollectMissingCharacterFromLogForTests(string logString, LogType type)
        {
            return TryCollectMissingCharactersFromLog(logString, type);
        }

        private static void OnLogMessage(string logString, string stackTrace, LogType type)
        {
            TryCollectMissingCharactersFromLog(logString, type);
        }

        private static bool TryCollectMissingCharactersFromLog(string logString, LogType type)
        {
            if (type != LogType.Warning || string.IsNullOrEmpty(logString))
            {
                return false;
            }

            if (!logString.Contains("was not found", StringComparison.OrdinalIgnoreCase) ||
                !logString.Contains(TargetFontAssetName, StringComparison.Ordinal))
            {
                return false;
            }

            var changed = false;
            foreach (Match match in MissingUnicodeRegex.Matches(logString))
            {
                if (!TryParseMissingCodePoint(match, out var codePoint))
                {
                    continue;
                }

                if (ShouldIgnoreCodePoint(codePoint))
                {
                    continue;
                }

                if (ExistingCodePoints.Add(codePoint))
                {
                    changed = true;
                    Log.Warning($"[MissingCharCollector] 收集到缺失字符：{char.ConvertFromUtf32(codePoint)} (\\u{match.Groups[1].Value})");
                }
            }

            if (changed)
            {
                SaveCharset();
            }

            return changed;
        }

        private static bool TryParseMissingCodePoint(Match match, out int codePoint)
        {
            codePoint = Convert.ToInt32(match.Groups[1].Value, 16);
            if (!match.Groups[2].Success)
            {
                return !IsSurrogateCodePoint(codePoint);
            }

            var high = (char)codePoint;
            var low = (char)Convert.ToInt32(match.Groups[2].Value, 16);
            if (!char.IsHighSurrogate(high) || !char.IsLowSurrogate(low))
            {
                return false;
            }

            codePoint = char.ConvertToUtf32(high, low);
            return true;
        }

        private static void LoadCharset()
        {
            ExistingCodePoints.Clear();
            if (!File.Exists(CharsetPath))
            {
                return;
            }

            var content = File.ReadAllText(CharsetPath, Encoding.UTF8);
            for (var index = 0; index < content.Length; index++)
            {
                var codePoint = (int)content[index];
                if (char.IsHighSurrogate(content[index]) &&
                    index < content.Length - 1 &&
                    char.IsLowSurrogate(content[index + 1]))
                {
                    codePoint = char.ConvertToUtf32(content[index], content[index + 1]);
                    index++;
                }

                if (!ShouldIgnoreCodePoint(codePoint))
                {
                    ExistingCodePoints.Add(codePoint);
                }
            }
        }

        private static void SaveCharset()
        {
            var directory = Path.GetDirectoryName(CharsetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = string.Concat(ExistingCodePoints
                .OrderBy(codePoint => codePoint)
                .Select(char.ConvertFromUtf32));
            File.WriteAllText(CharsetPath, content, Encoding.UTF8);
        }

        private static bool ShouldIgnoreCodePoint(int codePoint)
        {
            if (codePoint == 0xFEFF)
            {
                return true;
            }

            if (IsSurrogateCodePoint(codePoint))
            {
                return true;
            }

            if (codePoint <= char.MaxValue)
            {
                var character = (char)codePoint;
                if (char.IsControl(character))
                {
                    return true;
                }

                if (char.IsWhiteSpace(character) && codePoint != 0x20)
                {
                    return true;
                }

            }

            return codePoint >= 0xE000 && codePoint <= 0xF8FF ||
                codePoint >= 0xF0000 && codePoint <= 0xFFFFD ||
                codePoint >= 0x100000 && codePoint <= 0x10FFFD;
        }

        private static bool IsSurrogateCodePoint(int codePoint)
        {
            return codePoint >= 0xD800 && codePoint <= 0xDFFF;
        }
    }
}
