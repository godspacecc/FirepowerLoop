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
        private const int SchemaVersion = 3;
        private const int PasswordAlgorithmVersion = 1;
        private const int PasswordIterations = 600000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        [EntitySystem]
        private static void Awake(this OnlineAccountComponent self)
        {
            self.RemainingFailSaveCount = Math.Max(0, Options.Instance.OnlineProfileFailSaveCount);
            if (self.RemainingFailSaveCount > 0)
                Log.Warning($"online profile save failure injection enabled count={self.RemainingFailSaveCount}");
        }

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
            if (snapshot.SchemaVersion == 1)
            {
                // Version 1 predates online inventory. Treating migrated accounts as already supplied prevents a legacy account from receiving a retroactive starter grant.
                foreach (OnlinePlayerProfileRecord profile in snapshot.Players)
                {
                    profile.HasReceivedOnlineStarterSupplies = true;
                    profile.OnlineInventory = OnlineInventoryRecordHelper.Empty(12);
                }
                snapshot.SchemaVersion = SchemaVersion;
                ValidateSnapshot(snapshot);
                self.Commit(snapshot);
                return;
            }
            if (snapshot.SchemaVersion == 2)
            {
                // 在未发布的候选快照中迁移；校验和原子文件替换成功前不更新内存权威。
                using JsonDocument document = JsonDocument.Parse(json);
                foreach (JsonElement player in document.RootElement.GetProperty("Players").EnumerateArray())
                {
                    long id = player.GetProperty("PlayerId").GetInt64();
                    var profile = snapshot.Players.Single(x => x.PlayerId == id);
                    MigrateHeldInventory(profile.OnlineInventory, player.GetProperty("OnlineInventory").GetProperty("Equipment"));
                }
                snapshot.SchemaVersion = SchemaVersion;
                ValidateSnapshot(snapshot);
                self.Commit(snapshot);
                return;
            }
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
                    HasReceivedOnlineStarterSupplies = false,
                    OnlineInventory = OnlineInventoryRecordHelper.Empty(ServerConfig.Instance.OnlineWorldRule.BagCapacity),
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
                    HasReceivedOnlineStarterSupplies = false,
                    OnlineInventory = OnlineInventoryRecordHelper.Empty(ServerConfig.Instance.OnlineWorldRule.BagCapacity),
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

        public static OnlinePlayerProfileRecord GetProfile(this OnlineAccountComponent self, long accountId, long playerId)
        {
            return self.Snapshot.Players.FirstOrDefault(p => p.AccountId == accountId && p.PlayerId == playerId);
        }

        public static (int Error, string Message, long Revision) SaveProfile(
                this OnlineAccountComponent self,
                long accountId,
                long playerId,
                string displayName,
                string operationId,
                long expectedRevision,
                OnlineInventoryRecord inventory,
                bool hasReceivedOnlineStarterSupplies)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                return (ErrorCode.ERR_OnlineWorldOperationInvalid, "OperationId is required", expectedRevision);

            string fingerprint = OnlineInventoryRecordHelper.Fingerprint(inventory, hasReceivedOnlineStarterSupplies, expectedRevision, displayName);
            if (!self.SaveOperations.TryGetValue(playerId, out Dictionary<string, OnlineProfileSaveOperationRecord> playerOperations))
            {
                playerOperations = new Dictionary<string, OnlineProfileSaveOperationRecord>(StringComparer.Ordinal);
                self.SaveOperations.Add(playerId, playerOperations);
            }
            if (playerOperations.TryGetValue(operationId, out OnlineProfileSaveOperationRecord completed))
            {
                if (!string.Equals(completed.Fingerprint, fingerprint, StringComparison.Ordinal))
                    return (ErrorCode.ERR_OnlineWorldOperationConflict, "OperationId candidate conflict", completed.Revision);
                return (completed.Error, completed.Message, completed.Revision);
            }

            OnlineIdentityStoreSnapshot next = Clone(self.Snapshot);
            OnlinePlayerProfileRecord profile = next.Players.FirstOrDefault(p => p.PlayerId == playerId && p.AccountId == accountId);
            if (profile == null)
            {
                return (ErrorCode.ERR_OnlineServiceUnavailable, "Player profile not found", expectedRevision);
            }
            if (profile.Revision != expectedRevision)
                return (ErrorCode.ERR_OnlineWorldInventoryRevisionConflict, "Profile revision conflict", profile.Revision);
            try { ValidateInventory(inventory, $"PlayerId={playerId}"); }
            catch (InvalidOperationException e) { return (ErrorCode.ERR_OnlineWorldOperationInvalid, e.Message, profile.Revision); }
            if (inventory.Revision < profile.OnlineInventory.Revision)
                return (ErrorCode.ERR_OnlineWorldInventoryRevisionConflict, "Inventory revision is older than profile", profile.Revision);
            if (self.RemainingFailSaveCount > 0)
            {
                self.RemainingFailSaveCount--;
                Log.Warning($"online profile save injected failure playerId={playerId} remaining={self.RemainingFailSaveCount}");
                return (ErrorCode.ERR_OnlineProfileSaveFailed, "资料保存失败，请重试", profile.Revision);
            }
            profile.DisplayName = displayName;
            profile.Revision++;
            profile.OnlineInventory = OnlineInventoryRecordHelper.Clone(inventory);
            profile.HasReceivedOnlineStarterSupplies = hasReceivedOnlineStarterSupplies;
            profile.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            ValidateSnapshot(next);
            self.Commit(next);
            OnlineProfileSaveOperationRecord operation = new()
            {
                Fingerprint = fingerprint,
                Error = ErrorCode.ERR_Success,
                Message = string.Empty,
                Revision = profile.Revision,
                CompletedAt = TimeInfo.Instance.ServerNow(),
            };
            playerOperations.Add(operationId, operation);
            TrimOperations(playerOperations, operation.CompletedAt);
            return (operation.Error, operation.Message, operation.Revision);
        }

        private static void TrimOperations(Dictionary<string, OnlineProfileSaveOperationRecord> operations, long now)
        {
            if (operations.Count <= 128) return;
            foreach (string key in operations.Where(x => now - x.Value.CompletedAt >= 600000).OrderBy(x => x.Value.CompletedAt)
                         .Take(operations.Count - 128).Select(x => x.Key).ToArray()) operations.Remove(key);
        }

        /// <summary>跨库存与手持引用校验，不接受隐藏装备或未声明的容量增长。</summary>
        public static void ValidateInventory(OnlineInventoryRecord inventory, string context)
        {
            if (inventory == null || inventory.Slots == null || inventory.Equipment == null) throw new InvalidOperationException($"Invalid inventory ({context})");
            int capacity = ServerConfig.Instance.OnlineWorldRule.BagCapacity + inventory.MigrationExtraCapacity;
            if (inventory.MigrationExtraCapacity < 0 || inventory.Capacity != capacity || inventory.Revision <= 0) throw new InvalidOperationException($"Invalid capacity/revision ({context})");
            HashSet<int> indices = new();
            foreach (var slot in inventory.Slots)
            {
                Item item = ServerConfig.Instance.GetItem(slot.ItemId);
                if (slot.SlotIndex < 0 || slot.SlotIndex >= capacity || !indices.Add(slot.SlotIndex) || slot.Amount <= 0 || slot.Amount > item.MaxStackAmount || slot.LoadedAmmo < 0) throw new InvalidOperationException($"Invalid slot ({context})");
                if (slot.LoadedAmmo > 0 && (item.ItemType != ItemType.Weapon || slot.Amount != 1 || slot.LoadedAmmo > ServerConfig.Instance.GetWeapon(slot.ItemId).MagazineCapacity)) throw new InvalidOperationException($"Invalid loaded ammo ({context})");
            }
            foreach(var pair in OnlineInventoryRecordHelper.ClothingItems(inventory))
            {
                if(pair.Item1==0)continue;
                var garment=ServerConfig.Instance.GetItem(pair.Item1);
                if(garment.ItemType!=ItemType.Clothing||garment.WearSlot!=pair.Item2||garment.MaxStackAmount!=1||string.IsNullOrWhiteSpace(garment.AppearanceId))throw new InvalidOperationException($"Invalid clothing ({context})");
            }
            var held = inventory.Equipment;
            if (held.SchemaVersion != 1 || held.ItemId == 0 && held.SourceSlotIndex != -1 || held.ItemId != 0 && (ServerConfig.Instance.GetItem(held.ItemId).ItemType != ItemType.Weapon || OnlineInventoryRecordHelper.HeldStack(inventory) == null)) throw new InvalidOperationException($"Invalid held reference ({context})");
        }

        /// <summary>旧账号仅迁移一次；先合堆/填空格，必要时只增加回填所需格数。</summary>
        private static void MigrateHeldInventory(OnlineInventoryRecord inventory, JsonElement legacy)
        {
            int melee = legacy.GetProperty("MeleeWeaponItemId").GetInt32(), ranged = legacy.GetProperty("RangedWeaponItemId").GetInt32();
            int active = legacy.GetProperty("ActiveSlot").GetInt32(), ammo = legacy.GetProperty("RangedLoadedAmmo").GetInt32();
            if (inventory.Capacity != ServerConfig.Instance.OnlineWorldRule.BagCapacity || active < 0 || active > 2 || ammo < 0 || ranged == 0 && ammo != 0 || active == 2 && ranged == 0 || active == 0 && (melee != 0 || ranged != 0)) throw new InvalidOperationException("Invalid legacy equipment");
            foreach (var pair in new[] { (melee, WeaponKind.Melee), (ranged, WeaponKind.Projectile) })
                if (pair.Item1 != 0 && (ServerConfig.Instance.GetItem(pair.Item1).ItemType != ItemType.Weapon || ServerConfig.Instance.GetWeapon(pair.Item1).WeaponKind != pair.Item2)) throw new InvalidOperationException("Invalid legacy weapon");
            if (ranged != 0 && ammo > ServerConfig.Instance.GetWeapon(ranged).MagazineCapacity) throw new InvalidOperationException("Invalid legacy ammo");
            inventory.Equipment = new OnlineEquipmentRecord();
            ValidateInventory(inventory, "legacy inventory");
            int meleeSlot = AddLegacyItem(inventory, melee, melee == 0 ? 0 : 1), rangedSlot = AddLegacyItem(inventory, ranged, ranged == 0 ? 0 : 1);
            if (ammo > 0) AddLegacyItem(inventory, ServerConfig.Instance.GetWeapon(ranged).AmmoItemId, ammo);
            int id = active == 1 ? melee : active == 2 ? ranged : 0;
            if (id > 0) { inventory.Equipment.ItemId = id; inventory.Equipment.SourceSlotIndex = active == 1 ? meleeSlot : rangedSlot; }
        }
        private static int AddLegacyItem(OnlineInventoryRecord inventory, int id, int amount)
        {
            if (amount == 0) return -1;
            int max = ServerConfig.Instance.GetItem(id).MaxStackAmount, first = -1;
            foreach (var slot in inventory.Slots.Where(x => x.ItemId == id && x.Amount < max).OrderBy(x => x.SlotIndex))
            { int take = Math.Min(max - slot.Amount, amount); slot.Amount += take; amount -= take; first = slot.SlotIndex; if (amount == 0) return first; }
            while (amount > 0)
            {
                int index = 0; while (inventory.Slots.Any(x => x.SlotIndex == index)) index++;
                if (index >= inventory.Capacity) { inventory.Capacity++; inventory.MigrationExtraCapacity++; }
                int take = Math.Min(max, amount); inventory.Slots.Add(new OnlineItemSlotRecord { SlotIndex=index, ItemId=id, Amount=take }); amount-=take; if(first<0)first=index;
            }
            return first;
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
                ValidateInventory(profile.OnlineInventory, $"PlayerId={profile.PlayerId}");
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
