using System;
using System.IO;
using Newtonsoft.Json;
using QFramework;
using UnityEngine;

namespace Game
{
    public interface IOnlineAccountPreferenceStore : IUtility
    {
        string LoadLastUserName();
        bool SaveLastUserName(string userName, out string error);
    }

    public sealed class OnlineAccountPreferenceStore : IOnlineAccountPreferenceStore
    {
        private readonly string rootDirectory;

        public OnlineAccountPreferenceStore() : this(Application.persistentDataPath)
        {
        }

        internal OnlineAccountPreferenceStore(string rootDirectory)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? Application.persistentDataPath : rootDirectory;
        }

        public string DirectoryPath => Path.Combine(rootDirectory, "Online");
        public string FilePath => Path.Combine(DirectoryPath, "online-account-preferences.json");
        public string TempPath => Path.Combine(DirectoryPath, "online-account-preferences.tmp.json");

        public string LoadLastUserName()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return string.Empty;
                }

                var data = JsonConvert.DeserializeObject<OnlineAccountPreferenceData>(File.ReadAllText(FilePath));
                return data != null && data.version == 1 ? data.lastUserName ?? string.Empty : string.Empty;
            }
            catch
            {
                Debug.LogWarning("[OnlineAccountPreferenceStore] 偏好文件无效；使用空用户名。");
                return string.Empty;
            }
        }

        public bool SaveLastUserName(string userName, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var json = JsonConvert.SerializeObject(new OnlineAccountPreferenceData { version = 1, lastUserName = userName ?? string.Empty }, Formatting.Indented);
                File.WriteAllText(TempPath, json);
                if (File.Exists(FilePath))
                {
                    File.Replace(TempPath, FilePath, null, true);
                }
                else
                {
                    File.Move(TempPath, FilePath);
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name;
                Debug.LogWarning($"[OnlineAccountPreferenceStore] 保存在线账户偏好失败：{error}");
                try
                {
                    if (File.Exists(TempPath))
                    {
                        File.Delete(TempPath);
                    }
                }
                catch (Exception cleanupException)
                {
                    Debug.LogWarning($"[OnlineAccountPreferenceStore] 删除临时偏好文件失败：{cleanupException}");
                }
                return false;
            }
        }

        private sealed class OnlineAccountPreferenceData
        {
            public int version;
            public string lastUserName;
        }
    }
}
