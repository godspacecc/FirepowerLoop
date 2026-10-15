using System;
using System.IO;
using QFramework;
using UnityEngine;

namespace Game
{
    public interface IWeaponPoseFileStore : IUtility
    {
        string FilePath { get; }
        WeaponPoseSettingsData Load(out string warning);
        bool Save(WeaponPoseSettingsData data, out string error);
    }

    /// <summary>只负责本机 JSON 文件读写；临时文件验证后替换，保留上一份有效配置。</summary>
    public sealed class WeaponPoseFileStore : IWeaponPoseFileStore
    {
        public string FilePath { get; }
        public WeaponPoseFileStore(string rootDirectory = null)
        {
            FilePath = Path.Combine(rootDirectory ?? Application.persistentDataPath, "Settings", "weapon-poses.json");
        }

        public WeaponPoseSettingsData Load(out string warning)
        {
            warning = null;
            if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak")) return new WeaponPoseSettingsData();
            if (Read(FilePath, out var data, out warning)) return data;
            if (Read(FilePath + ".bak", out data, out var backupError))
            {
                warning = "姿态配置损坏，已读取备份：" + warning;
                return data;
            }
            warning = "未能读取姿态配置，使用资源默认值：" + warning + "; " + backupError;
            return new WeaponPoseSettingsData();
        }

        public bool Save(WeaponPoseSettingsData data, out string error)
        {
            error = null;
            if (!WeaponPoseSettingsSystem.Validate(data, out error)) return false;
            var temporary = FilePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (!Read(temporary, out _, out error)) return false;
                if (File.Exists(FilePath))
                {
                    try { File.Replace(temporary, FilePath, FilePath + ".bak"); }
                    catch (PlatformNotSupportedException)
                    {
                        // 少数运行平台没有 Replace；先保留备份，启动读取仍可恢复中断写入。
                        File.Copy(FilePath, FilePath + ".bak", true);
                        File.Delete(FilePath);
                        File.Move(temporary, FilePath);
                    }
                }
                else File.Move(temporary, FilePath);
                return true;
            }
            catch (Exception exception) { error = exception.Message; return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception) { /* 清理失败不能覆盖原保存错误；临时文件可在下次保存覆盖。 */ }
            }
        }

        private static bool Read(string path, out WeaponPoseSettingsData data, out string error)
        {
            data = null;
            try
            {
                return WeaponPoseSettingsSystem.TryParse(File.ReadAllText(path), out data, out error);
            }
            catch (Exception exception) { error = exception.Message; return false; }
        }
    }
}
