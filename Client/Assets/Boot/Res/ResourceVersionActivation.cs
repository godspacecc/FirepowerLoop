using System;
using System.IO;
using UnityEngine;

namespace SharedBoot
{
    /// <summary>管理本次候选和上次就绪的资源版本，只有 Main 就绪后才提交。</summary>
    internal sealed class ResourceVersionActivation
    {
        private readonly string _goodKey;
        private readonly string _pendingKey;
        private readonly string _rejectedKey;
        private readonly string _legacyGoodKey;
        private FileStream _trialLock;

        internal ResourceVersionActivation(string goodKey, string activationKey, string legacyGoodKey = null)
        {
            _goodKey = goodKey;
            _pendingKey = activationKey + ".pending";
            _rejectedKey = activationKey + ".rejected";
            _legacyGoodKey = legacyGoodKey;
        }

        private string ConfirmedVersion => PlayerPrefs.GetString(_goodKey);

        internal string LastGoodVersion
        {
            get
            {
                string confirmed = ConfirmedVersion;
                return string.IsNullOrEmpty(confirmed) && _legacyGoodKey != null
                    ? PlayerPrefs.GetString(_legacyGoodKey) : confirmed;
            }
        }

        internal bool CanTry(string version) =>
            !string.IsNullOrEmpty(version) &&
            !string.Equals(version, PlayerPrefs.GetString(_rejectedKey), StringComparison.Ordinal);

        /// <summary>只有取得启动锁后，才能将上次未完成的候选视为中断。</summary>
        internal bool RecoverInterrupted()
        {
            using var recoveryLock = TryOpenLock();
            return recoveryLock != null && RecoverPending();
        }

        private bool RecoverPending()
        {
            string version = PlayerPrefs.GetString(_pendingKey);
            if (string.IsNullOrEmpty(version))
                return false;

            RecordFailure(version);
            return true;
        }

        /// <summary>在资源选择阶段登记候选；锁被占用或版本已被拒绝时交给调用方回退。</summary>
        internal bool Begin(string version)
        {
            if (_trialLock != null)
                return string.Equals(version, PlayerPrefs.GetString(_pendingKey), StringComparison.Ordinal);
            if (!CanTry(version))
                return false;
            if (string.Equals(version, ConfirmedVersion, StringComparison.Ordinal))
                return true;

            var trialLock = TryOpenLock();
            if (trialLock == null)
                return false;

            try
            {
                RecoverPending();
                if (!CanTry(version))
                    return false;
                if (string.Equals(version, ConfirmedVersion, StringComparison.Ordinal))
                    return true;

                PlayerPrefs.SetString(_pendingKey, version);
                PlayerPrefs.Save();
                _trialLock = trialLock;
                return true;
            }
            finally
            {
                if (_trialLock == null)
                    trialLock.Dispose();
            }
        }

        /// <summary>玩家修改资源服时放弃当前选择，保留该版本后续重新尝试的资格。</summary>
        internal void Cancel()
        {
            if (_trialLock == null)
                return;

            try
            {
                PlayerPrefs.DeleteKey(_pendingKey);
                PlayerPrefs.Save();
            }
            finally
            {
                End();
            }
        }

        internal void Commit(string version) => Complete(version, ready: true);

        internal void Reject(string version) => Complete(version, ready: false);

        private void Complete(string version, bool ready)
        {
            if (_trialLock == null)
                return;

            try
            {
                if (!string.Equals(version, PlayerPrefs.GetString(_pendingKey), StringComparison.Ordinal))
                    return;

                if (!ready)
                {
                    RecordFailure(version);
                    return;
                }

                PlayerPrefs.SetString(_goodKey, version);
                if (_legacyGoodKey != null)
                    PlayerPrefs.SetString(_legacyGoodKey, version);
                PlayerPrefs.DeleteKey(_pendingKey);
                PlayerPrefs.DeleteKey(_rejectedKey);
                PlayerPrefs.Save();
            }
            finally
            {
                End();
            }
        }

        private void RecordFailure(string version)
        {
            if (!string.Equals(version, ConfirmedVersion, StringComparison.Ordinal))
            {
                PlayerPrefs.SetString(_rejectedKey, version);
                if (_legacyGoodKey != null && string.Equals(version, PlayerPrefs.GetString(_legacyGoodKey),
                        StringComparison.Ordinal))
                    PlayerPrefs.DeleteKey(_legacyGoodKey);
            }

            PlayerPrefs.DeleteKey(_pendingKey);
            PlayerPrefs.Save();
        }

        /// <summary>释放当前进程的启动锁，留待下一次启动处理尚未确认的候选。</summary>
        internal void End()
        {
            _trialLock?.Dispose();
            _trialLock = null;
        }

        private FileStream TryOpenLock()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, $"BootActivation-{Hash128.Compute(_pendingKey)}.lock");
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException error)
            {
                Debug.LogWarning($"[Boot] 无法打开资源启动锁：{error.Message}");
                return null;
            }
        }

        /// <summary>删除旧宿主的版本记录，由宿主替换流程与新宿主版本号一并保存。</summary>
        internal void Clear(Func<bool> invalidateCache)
        {
            using var versionLock = TryOpenLock();
            if (versionLock == null)
                throw new IOException("无法获取资源启动锁，旧宿主缓存尚未清理。");
            if (!invalidateCache())
                return;

            PlayerPrefs.DeleteKey(_goodKey);
            PlayerPrefs.DeleteKey(_pendingKey);
            PlayerPrefs.DeleteKey(_rejectedKey);
            if (_legacyGoodKey != null)
                PlayerPrefs.DeleteKey(_legacyGoodKey);
            PlayerPrefs.Save();
        }
    }
}
