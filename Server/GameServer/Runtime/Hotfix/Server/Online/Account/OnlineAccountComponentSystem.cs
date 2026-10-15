using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ET.Server
{
    [EntitySystemOf(typeof(OnlineAccountComponent))]
    [FriendOf(typeof(OnlineAccountComponent))]
    public static partial class OnlineAccountComponentSystem
    {
        private const int SchemaVersion = 4;
        private const int PasswordAlgorithmVersion = 1;
        private const int PasswordIterations = 600000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        [EntitySystem]
        private static void Awake(this OnlineAccountComponent self)
        {
        }

        /// <summary>仅加载本工程当前身份格式；不导入或迁移其他项目的玩家状态。</summary>
        public static void LoadStore(this OnlineAccountComponent self, string storePath)
        {
            if (string.IsNullOrWhiteSpace(storePath))
            {
                throw new InvalidOperationException("OnlineStore path is empty.");
            }

            self.StorePath = Path.GetFullPath(storePath);
            string parent = Path.GetDirectoryName(self.StorePath);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException("OnlineStore parent directory is invalid.");
            }

            Directory.CreateDirectory(parent);

            if (!File.Exists(self.StorePath))
            {
                self.Snapshot = new OnlineIdentityStoreSnapshot();
                self.Commit(self.Snapshot);
                return;
            }

            string json = File.ReadAllText(self.StorePath);
            OnlineIdentityStoreSnapshot snapshot = JsonSerializer.Deserialize<OnlineIdentityStoreSnapshot>(json, JsonOptions()) ??
                    throw new InvalidOperationException("OnlineStore JSON is empty.");
            ValidateSnapshot(snapshot);
            self.Snapshot = snapshot;
        }

        public static (int Error, string Message, long AccountId) Register(this OnlineAccountComponent self, string userName, string password)
        {
            int userNameError = ValidateUserName(userName, out string normalized, out string display);
            if (userNameError != ErrorCode.ERR_Success)
            {
                return (userNameError, "用户名格式非法", 0);
            }

            int passwordError = ValidatePassword(password);
            if (passwordError != ErrorCode.ERR_Success)
            {
                return (passwordError, "密码格式非法", 0);
            }

            OnlineIdentityStoreSnapshot next = Clone(self.Snapshot);
            if (next.Accounts.Any(a => a.NormalizedUserName == normalized))
            {
                return (ErrorCode.ERR_OnlineUserNameExists, "用户名已存在", 0);
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = HashPassword(password, salt, PasswordIterations);
            long accountId = next.NextAccountId++;
            string now = DateTime.UtcNow.ToString("O");
            next.Accounts.Add(new OnlineAccountRecord
            {
                AccountId = accountId,
                NormalizedUserName = normalized,
                DisplayUserName = display,
                AlgorithmVersion = PasswordAlgorithmVersion,
                Iterations = PasswordIterations,
                SaltBase64 = Convert.ToBase64String(salt),
                HashBase64 = Convert.ToBase64String(hash),
                PlayerId = 0,
                CreatedAtUtc = now,
            });
            ValidateSnapshot(next);
            self.Commit(next);
            return (ErrorCode.ERR_Success, string.Empty, accountId);
        }

        public static (int Error, string Message, long AccountId, long PlayerId, string DisplayName, long Revision, bool IsNewAccount) Login(
                this OnlineAccountComponent self,
                string userName,
                string password)
        {
            int userNameError = ValidateUserName(userName, out string normalized, out string display);
            if (userNameError != ErrorCode.ERR_Success)
            {
                return (userNameError, "用户名格式非法", 0, 0, string.Empty, 0, false);
            }

            int passwordError = ValidatePassword(password);
            if (passwordError != ErrorCode.ERR_Success)
            {
                return (passwordError, "密码格式非法", 0, 0, string.Empty, 0, false);
            }

            OnlineAccountRecord existing = self.Snapshot.Accounts.FirstOrDefault(a => a.NormalizedUserName == normalized);
            if (existing == null)
            {
                // LOGIN-02: auto-create account + first player in one commit
                OnlineIdentityStoreSnapshot next = Clone(self.Snapshot);
                byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
                byte[] hash = HashPassword(password, salt, PasswordIterations);
                long accountId = next.NextAccountId++;
                long playerId = next.NextPlayerId++;
                string now = DateTime.UtcNow.ToString("O");
                var account = new OnlineAccountRecord
                {
                    AccountId = accountId,
                    NormalizedUserName = normalized,
                    DisplayUserName = display,
                    AlgorithmVersion = PasswordAlgorithmVersion,
                    Iterations = PasswordIterations,
                    SaltBase64 = Convert.ToBase64String(salt),
                    HashBase64 = Convert.ToBase64String(hash),
                    PlayerId = playerId,
                    CreatedAtUtc = now,
                };
                next.Accounts.Add(account);
                var profile = new OnlinePlayerProfileRecord
                {
                    PlayerId = playerId,
                    AccountId = accountId,
                    DisplayName = display,
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                next.Players.Add(profile);
                ValidateSnapshot(next);
                string autoTemp = self.NewTempPath();
                try
                {
                    self.Commit(next, autoTemp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LOGIN-AUTO-COMMIT-FAIL | account={normalized} | {ex.GetType().Name}: {ex.Message}");
                    CleanupTempFile(autoTemp);
                    return (ErrorCode.ERR_OnlineServiceUnavailable, "持久化失败", 0, 0, string.Empty, 0, false);
                }
                return (ErrorCode.ERR_Success, string.Empty, accountId, playerId, profile.DisplayName, profile.Revision, true);
            }

            // LOGIN-04: existing account with wrong password
            if (!VerifyPassword(existing, password))
            {
                return (ErrorCode.ERR_OnlineAccountOrPasswordError, "账号或密码错误", 0, 0, string.Empty, 0, false);
            }

            // LOGIN-03: existing account, correct password
            OnlineIdentityStoreSnapshot updated = Clone(self.Snapshot);
            OnlineAccountRecord accountRecord = updated.Accounts.First(a => a.AccountId == existing.AccountId);
            OnlinePlayerProfileRecord profileRecord;
            if (accountRecord.PlayerId == 0)
            {
                long playerId = updated.NextPlayerId++;
                string now = DateTime.UtcNow.ToString("O");
                accountRecord.PlayerId = playerId;
                profileRecord = new OnlinePlayerProfileRecord
                {
                    PlayerId = playerId,
                    AccountId = accountRecord.AccountId,
                    DisplayName = accountRecord.DisplayUserName,
                    Revision = 1,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                updated.Players.Add(profileRecord);
                ValidateSnapshot(updated);
                string existTemp = self.NewTempPath();
                try
                {
                    self.Commit(updated, existTemp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"LOGIN-EXIST-COMMIT-FAIL | accountId={existing.AccountId} | {ex.GetType().Name}: {ex.Message}");
                    CleanupTempFile(existTemp);
                    return (ErrorCode.ERR_OnlineServiceUnavailable, "持久化失败", 0, 0, string.Empty, 0, false);
                }
            }
            else
            {
                profileRecord = updated.Players.First(p => p.PlayerId == accountRecord.PlayerId);
            }

            return (ErrorCode.ERR_Success, string.Empty, accountRecord.AccountId, accountRecord.PlayerId, profileRecord.DisplayName, profileRecord.Revision, false);
        }

        private static int ValidateUserName(string userName, out string normalized, out string display)
        {
            display = (userName ?? string.Empty).Trim();
            normalized = display.ToLowerInvariant();
            return Regex.IsMatch(display, "^[A-Za-z0-9_]{3,20}$") ? ErrorCode.ERR_Success : ErrorCode.ERR_OnlineInvalidUserName;
        }

        private static int ValidatePassword(string password)
        {
            if (password == null || password.Length > 8)
            {
                return ErrorCode.ERR_OnlineInvalidPassword;
            }

            for (int i = 0; i < password.Length; i++)
            {
                char c = password[i];
                bool valid = c >= 'A' && c <= 'Z' ||
                             c >= 'a' && c <= 'z' ||
                             c >= '0' && c <= '9' ||
                             c == '_';
                if (!valid)
                {
                    return ErrorCode.ERR_OnlineInvalidPassword;
                }
            }

            return ErrorCode.ERR_Success;
        }

        private static byte[] HashPassword(string password, byte[] salt, int iterations)
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        }

        private static bool VerifyPassword(OnlineAccountRecord account, string password)
        {
            if (account.AlgorithmVersion != PasswordAlgorithmVersion)
            {
                return false;
            }

            byte[] salt = Convert.FromBase64String(account.SaltBase64);
            byte[] expected = Convert.FromBase64String(account.HashBase64);
            byte[] actual = HashPassword(password, salt, account.Iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static OnlineIdentityStoreSnapshot Clone(OnlineIdentityStoreSnapshot snapshot)
        {
            return JsonSerializer.Deserialize<OnlineIdentityStoreSnapshot>(JsonSerializer.Serialize(snapshot, JsonOptions()), JsonOptions());
        }

        private static void Commit(this OnlineAccountComponent self, OnlineIdentityStoreSnapshot snapshot, string temp)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions());

            using (FileStream fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(true);
            }

            if (File.Exists(self.StorePath))
            {
                File.Replace(temp, self.StorePath, null);
            }
            else
            {
                File.Move(temp, self.StorePath);
            }

            self.Snapshot = snapshot;
        }

        private static void Commit(this OnlineAccountComponent self, OnlineIdentityStoreSnapshot snapshot)
        {
            string parent = Path.GetDirectoryName(self.StorePath);
            Directory.CreateDirectory(parent);
            string temp = Path.Combine(parent, $"{Path.GetFileName(self.StorePath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                self.Commit(snapshot, temp);
            }
            catch
            {
                CleanupTempFile(temp);
                throw;
            }
        }

        private static string NewTempPath(this OnlineAccountComponent self)
        {
            string parent = Path.GetDirectoryName(self.StorePath);
            Directory.CreateDirectory(parent);
            return Path.Combine(parent, $"{Path.GetFileName(self.StorePath)}.{Guid.NewGuid():N}.tmp");
        }

        private static void CleanupTempFile(string tempPath)
        {
            if (string.IsNullOrWhiteSpace(tempPath))
            {
                return;
            }

            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }

        private static void ValidateSnapshot(OnlineIdentityStoreSnapshot snapshot)
        {
            if (snapshot.SchemaVersion != SchemaVersion)
            {
                throw new InvalidOperationException($"Unsupported OnlineStore schema version: {snapshot.SchemaVersion}");
            }
            if (snapshot.NextAccountId <= 0)
            {
                throw new InvalidOperationException("NextAccountId must be positive.");
            }
            if (snapshot.NextPlayerId <= 0)
            {
                throw new InvalidOperationException("NextPlayerId must be positive.");
            }

            HashSet<long> accountIds = new();
            HashSet<long> playerIds = new();
            HashSet<string> names = new(StringComparer.Ordinal);
            long maxAccountId = 0;
            long maxPlayerId = 0;

            foreach (OnlineAccountRecord account in snapshot.Accounts)
            {
                if (!accountIds.Add(account.AccountId))
                {
                    throw new InvalidOperationException($"Duplicate AccountId: {account.AccountId}");
                }
                maxAccountId = Math.Max(maxAccountId, account.AccountId);
                if (string.IsNullOrEmpty(account.NormalizedUserName) || !names.Add(account.NormalizedUserName))
                {
                    throw new InvalidOperationException($"Duplicate or empty user name: {account.NormalizedUserName}");
                }
                if (account.AlgorithmVersion != PasswordAlgorithmVersion)
                {
                    throw new InvalidOperationException($"Unsupported password algorithm version: {account.AlgorithmVersion}");
                }
                if (account.Iterations != PasswordIterations)
                {
                    throw new InvalidOperationException($"Invalid password iterations: {account.Iterations}");
                }
                ValidateBase64Bytes(account.SaltBase64, SaltBytes, $"SaltBase64 for account {account.AccountId}");
                ValidateBase64Bytes(account.HashBase64, HashBytes, $"HashBase64 for account {account.AccountId}");
                if (account.PlayerId != 0 && snapshot.Players.All(p => p.PlayerId != account.PlayerId || p.AccountId != account.AccountId))
                {
                    throw new InvalidOperationException($"Account player mapping mismatch: {account.AccountId}/{account.PlayerId}");
                }
            }

            foreach (OnlinePlayerProfileRecord profile in snapshot.Players)
            {
                if (!playerIds.Add(profile.PlayerId))
                {
                    throw new InvalidOperationException($"Duplicate PlayerId: {profile.PlayerId}");
                }
                maxPlayerId = Math.Max(maxPlayerId, profile.PlayerId);
                if (snapshot.Accounts.All(a => a.AccountId != profile.AccountId || a.PlayerId != profile.PlayerId))
                {
                    throw new InvalidOperationException($"Player account mapping mismatch: {profile.PlayerId}/{profile.AccountId}");
                }
            }

            if (snapshot.NextAccountId <= maxAccountId)
            {
                throw new InvalidOperationException($"NextAccountId must be greater than max AccountId: {snapshot.NextAccountId}/{maxAccountId}");
            }
            if (snapshot.NextPlayerId <= maxPlayerId)
            {
                throw new InvalidOperationException($"NextPlayerId must be greater than max PlayerId: {snapshot.NextPlayerId}/{maxPlayerId}");
            }
        }

        private static void ValidateBase64Bytes(string value, int expectedLength, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{fieldName} is empty.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(value);
            }
            catch (FormatException e)
            {
                throw new InvalidOperationException($"{fieldName} is not valid Base64.", e);
            }

            if (bytes.Length != expectedLength)
            {
                throw new InvalidOperationException($"{fieldName} length must be {expectedLength} bytes.");
            }
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions { WriteIndented = true };
        }
    }
}
