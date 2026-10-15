using System;
using System.Net;

namespace ET.Server
{
    [EntitySystemOf(typeof(OnlineTestComponent))]
    [FriendOf(typeof(OnlineTestComponent))]
    public static partial class OnlineTestComponentSystem
    {
        private const string Password = "Pass_123";

        [EntitySystem]
        private static void Awake(this OnlineTestComponent self)
        {
            self.Run().Coroutine();
        }

        private static async ETTask Run(this OnlineTestComponent self)
        {
            int failed = 0;
            try
            {
                string mode = Options.Instance.OnlineTestMode;
                if (string.Equals(mode, "ServerTestCmd", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunServerTestCmd();
                }
                else if (string.Equals(mode, "RestartCheck", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRestartCheck();
                }
                else if (string.Equals(mode, "ReplaceQuick", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunReplaceQuick();
                }
                else if (string.Equals(mode, "LifecycleOperationId", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunLifecycleOperationId();
                }
                else if (string.Equals(mode, "Room", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(mode, "RoomPrimary", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoom();
                }
                else if (string.Equals(mode, "RoomList", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoomList();
                }
                else if (string.Equals(mode, "Chat", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunChat();
                }
                else if (string.Equals(mode, "ChatRetry", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunChatRetry();
                }
                else if (string.Equals(mode, "ChatCloseRetry", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunChatCloseRetry();
                }
                else if (string.Equals(mode, "RoomClosing", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoomFaultClose();
                }
                else if (string.Equals(mode, "RoomRetry", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoomRetry();
                }
                else if (string.Equals(mode, "RoomTimeout", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoomTimeout();
                }
                else if (string.Equals(mode, "RoomRestartCheck", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoomRestartCheck();
                }
                else if (string.Equals(mode, "RoomRestartPrepare", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoomRestartPrepare();
                }
                else if (string.Equals(mode, "RoomFaultClose", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoomFaultClose();
                }
                else if (string.Equals(mode, "RoomFaultLeave", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunRoomFaultLeave();
                }
                else if (string.Equals(mode, "LoginEntry", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(mode, "LoginEntryOnly", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunLoginEntryOnly();
                }
                else if (string.Equals(mode, "LoginEntryCommitFailure", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunLoginEntryCommitFailure();
                }
                else if (string.Equals(mode, "LoginEntryRetry", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunLoginEntryRetry();
                }
                else if (string.Equals(mode, "Primary", StringComparison.OrdinalIgnoreCase))
                {
                    failed += await self.RunPrimary();
                }
                else
                {
                    throw new ArgumentException($"Unknown online test mode: {mode}");
                }
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine($"CLP-FATAL | Failed | 0ms | {e.GetType().Name}: {e.Message}");
            }

            Console.WriteLine(failed == 0 ? "ONLINE_LIFECYCLE_TEST_RESULT=Passed" : "ONLINE_LIFECYCLE_TEST_RESULT=Failed");
            Environment.Exit(failed == 0 ? 0 : 1);
        }

        private static async ETTask<int> RunLoginEntryCommitFailure(this OnlineTestComponent self)
        {
            int failed = 0;
            string user = string.IsNullOrWhiteSpace(Options.Instance.OnlineTestUser)
                    ? $"lf{DateTime.UtcNow:HHmmss}{Random.Shared.Next(1000, 9999)}"
                    : Options.Instance.OnlineTestUser;

            // LOGIN-AUTO-05: Store 被独占锁定时登录提交失败返回 200107
            failed += await self.Case("LOGIN-AUTO-05", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await session.Call(Login(user)) as G2C_Login;
                Ensure(login.Error == ErrorCode.ERR_OnlineServiceUnavailable, $"expected 200107 but got error={login.Error}");
                Ensure(login.AccountId == 0, $"AccountId should be 0 on failure, got {login.AccountId}");
                Ensure(login.PlayerId == 0, $"PlayerId should be 0 on failure, got {login.PlayerId}");
                Ensure(!login.IsNewAccount, $"IsNewAccount should be false on failure, got {login.IsNewAccount}");
                Console.WriteLine($"LOCK_FAIL_ERROR={login.Error}");
            });

            return failed;
        }

        private static async ETTask<int> RunLoginEntryRetry(this OnlineTestComponent self)
        {
            string user = string.IsNullOrWhiteSpace(Options.Instance.OnlineTestUser)
                    ? $"lr{DateTime.UtcNow:HHmmss}{Random.Shared.Next(1000, 9999)}"
                    : Options.Instance.OnlineTestUser;

            return await self.Case("LOGIN-AUTO-06", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await session.Call(Login(user)) as G2C_Login;
                Ensure(login.Error == ErrorCode.ERR_Success, $"retry login error={login.Error}");
                Ensure(login.AccountId > 0, $"AccountId invalid={login.AccountId}");
                Ensure(login.PlayerId > 0, $"PlayerId invalid={login.PlayerId}");
                Ensure(login.IsNewAccount, $"retry after failed commit should create the account once, got IsNewAccount={login.IsNewAccount}");
                Console.WriteLine($"RETRY_ACCOUNT_ID={login.AccountId}");
                Console.WriteLine($"RETRY_PLAYER_ID={login.PlayerId}");
            });
        }

        private static async ETTask<int> RunLoginEntryOnly(this OnlineTestComponent self)
        {
            int failed = 0;
            string user = string.IsNullOrWhiteSpace(Options.Instance.OnlineTestUser)
                    ? $"la{DateTime.UtcNow:HHmmss}{Random.Shared.Next(1000, 9999)}"
                    : Options.Instance.OnlineTestUser;

            // LOGIN-AUTO-01: 新账号首次登录自动建号
            long accountId = 0;
            long playerId = 0;
            failed += await self.Case("LOGIN-AUTO-01", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await session.Call(Login(user)) as G2C_Login;
                Ensure(login.Error == ErrorCode.ERR_Success, $"auto-create error={login.Error}");
                Ensure(login.IsNewAccount, $"IsNewAccount should be true, got {login.IsNewAccount}");
                Ensure(login.AccountId > 0, $"AccountId invalid={login.AccountId}");
                Ensure(login.PlayerId > 0, $"PlayerId invalid={login.PlayerId}");
                accountId = login.AccountId;
                playerId = login.PlayerId;
                Console.WriteLine($"AUTO_ACCOUNT_ID={accountId}");
                Console.WriteLine($"AUTO_PLAYER_ID={playerId}");
            });

            // LOGIN-AUTO-02: 同一账号再次登录IsNewAccount=false
            failed += await self.Case("LOGIN-AUTO-02", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await session.Call(Login(user)) as G2C_Login;
                Ensure(login.Error == ErrorCode.ERR_Success, $"re-login error={login.Error}");
                Ensure(!login.IsNewAccount, $"IsNewAccount should be false on re-login, got {login.IsNewAccount}");
                Ensure(login.AccountId == accountId, $"AccountId mismatch={login.AccountId} expected={accountId}");
                Ensure(login.PlayerId == playerId, $"PlayerId mismatch={login.PlayerId} expected={playerId}");
            });

            // LOGIN-AUTO-03: 错误密码返回200104
            failed += await self.Case("LOGIN-AUTO-03", async () =>
            {
                using Session session = self.CreateSession();
                byte[] before = self.ReadStoreBytes();
                using G2C_Login login = await session.Call(Login(user, "Wrong_99")) as G2C_Login;
                Ensure(login.Error == ErrorCode.ERR_OnlineAccountOrPasswordError, $"wrong password error={login.Error}");
                Ensure(login.AccountId == 0, $"AccountId should be 0 on wrong password, got {login.AccountId}");
                Ensure(login.PlayerId == 0, $"PlayerId should be 0 on wrong password, got {login.PlayerId}");
                byte[] after = self.ReadStoreBytes();
                Ensure(Convert.ToBase64String(before) == Convert.ToBase64String(after), "store changed after wrong password login");
            });

            // LOGIN-AUTO-04: 字段错误码验证（无效用户名、无效密码）
            failed += await self.Case("LOGIN-AUTO-04", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login badName = await session.Call(Login("ab")) as G2C_Login;
                Ensure(badName.Error == ErrorCode.ERR_OnlineInvalidUserName, $"bad name error={badName.Error}");

                using Session session2 = self.CreateSession();
                using G2C_Login longPass = await session2.Call(Login(UniqueUser("la04"), "123456789")) as G2C_Login;
                Ensure(longPass.Error == ErrorCode.ERR_OnlineInvalidPassword, $"long password error={longPass.Error}");

                using Session session3 = self.CreateSession();
                using G2C_Login spacePass = await session3.Call(Login(UniqueUser("la04s"), "abc def")) as G2C_Login;
                Ensure(spacePass.Error == ErrorCode.ERR_OnlineInvalidPassword, $"space password error={spacePass.Error}");
            });

            return failed;
        }

        private static async ETTask<int> RunPrimary(this OnlineTestComponent self)
        {
            int failed = 0;
            string user = string.IsNullOrWhiteSpace(Options.Instance.OnlineTestUser)
                    ? $"ad{DateTime.UtcNow:HHmmss}{Random.Shared.Next(1000, 9999)}"
                    : Options.Instance.OnlineTestUser;

            failed += await self.Case("CLP-01", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Register response = await session.Call(Register(user)) as G2C_Register;
                Ensure(response.Error == ErrorCode.ERR_Success && response.AccountId > 0, $"register error={response.Error}");
                Console.WriteLine($"PRIMARY_ACCOUNT_ID={response.AccountId}");
            });

            failed += await self.Case("CLP-02", async () =>
            {
                using Session a = self.CreateSession();
                using Session b = self.CreateSession();
                ETTask<IResponse> t1 = a.Call(Register(user.ToUpperInvariant()), 10000);
                ETTask<IResponse> t2 = b.Call(Register(user.ToLowerInvariant()), 10000);
                using G2C_Register r1 = await t1 as G2C_Register;
                using G2C_Register r2 = await t2 as G2C_Register;
                Ensure(r1.Error == ErrorCode.ERR_OnlineUserNameExists && r2.Error == ErrorCode.ERR_OnlineUserNameExists,
                    $"duplicate errors={r1.Error}/{r2.Error}");
            });

            failed += await self.Case("CLP-03", async () =>
            {
                using Session session = self.CreateSession();
                byte[] before = self.ReadStoreBytes();
                using G2C_Register badName = await session.Call(Register("ab")) as G2C_Register;
                using G2C_Register longPassword = await session.Call(Register(UniqueUser("adlong"), "123456789")) as G2C_Register;
                using G2C_Register spacePassword = await session.Call(Register(UniqueUser("adspace"), "abc def")) as G2C_Register;
                using G2C_Register chinesePassword = await session.Call(Register(UniqueUser("adcn"), "密码")) as G2C_Register;
                using G2C_Register symbolPassword = await session.Call(Register(UniqueUser("adsym"), "abc!")) as G2C_Register;
                using G2C_Register newlinePassword = await session.Call(Register(UniqueUser("adnl"), "\n")) as G2C_Register;
                using G2C_Register trailingNewlinePassword = await session.Call(Register(UniqueUser("adtnl"), "A\n")) as G2C_Register;
                using G2C_Register crlfPassword = await session.Call(Register(UniqueUser("adcrlf"), "\r\n")) as G2C_Register;
                byte[] after = self.ReadStoreBytes();
                Ensure(badName.Error == ErrorCode.ERR_OnlineInvalidUserName, $"bad name error={badName.Error}");
                Ensure(longPassword.Error == ErrorCode.ERR_OnlineInvalidPassword, $"long password error={longPassword.Error}");
                Ensure(spacePassword.Error == ErrorCode.ERR_OnlineInvalidPassword, $"space password error={spacePassword.Error}");
                Ensure(chinesePassword.Error == ErrorCode.ERR_OnlineInvalidPassword, $"chinese password error={chinesePassword.Error}");
                Ensure(symbolPassword.Error == ErrorCode.ERR_OnlineInvalidPassword, $"symbol password error={symbolPassword.Error}");
                Ensure(newlinePassword.Error == ErrorCode.ERR_OnlineInvalidPassword, $"newline password error={newlinePassword.Error}");
                Ensure(trailingNewlinePassword.Error == ErrorCode.ERR_OnlineInvalidPassword, $"trailing newline password error={trailingNewlinePassword.Error}");
                Ensure(crlfPassword.Error == ErrorCode.ERR_OnlineInvalidPassword, $"crlf password error={crlfPassword.Error}");
                Ensure(Convert.ToBase64String(before) == Convert.ToBase64String(after), "store changed after invalid registration requests");
            });

            failed += await self.Case("CLP-03A", async () =>
            {
                await self.EnsureRegisterAndLogin(UniqueUser("adempty"), string.Empty);
                await self.EnsureRegisterAndLogin(UniqueUser("adone"), "A");
                await self.EnsureRegisterAndLogin(UniqueUser("adeight"), "Abc_1234");
            });

            failed += await self.Case("CLP-04", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login unknown = await session.Call(Login($"{user}_none")) as G2C_Login;
                Ensure(unknown.Error == ErrorCode.ERR_Success && unknown.IsNewAccount, $"unknown auto-create error={unknown.Error} isNew={unknown.IsNewAccount}");
                using G2C_Login wrong = await session.Call(Login(user, "Pass_124")) as G2C_Login;
                Ensure(wrong.Error == ErrorCode.ERR_OnlineAccountOrPasswordError, $"wrong error={wrong.Error}");
            });

            long accountId = 0;
            long playerId = 0;
            long firstEpoch = 0;
            Session first = null;
            Session second = null;
            Session third = null;
            Session fourth = null;
            try
            {
                failed += await self.Case("CLP-05", async () =>
                {
                    first = self.CreateSession();
                    using G2C_Login login = await first.Call(Login(user)) as G2C_Login;
                    Ensure(login.Error == ErrorCode.ERR_Success, $"login error={login.Error}");
                    Ensure(login.AccountId > 0 && login.PlayerId > 0 && login.SessionEpoch == 1 && !login.IsPlayerActorReused,
                        $"ids/epoch/reuse invalid account={login.AccountId} player={login.PlayerId} epoch={login.SessionEpoch} reused={login.IsPlayerActorReused}");
                    accountId = login.AccountId;
                    playerId = login.PlayerId;
                    firstEpoch = login.SessionEpoch;
                    Console.WriteLine($"PRIMARY_PLAYER_ID={playerId}");
                });

                failed += await self.Case("CLP-07", async () =>
                {
                    int beforePushCount = self.SessionReplacedReasons.Count;
                    long oldSessionId = first.Id;
                    second = self.CreateSession();
                    using G2C_Login login = await second.Call(Login(user)) as G2C_Login;
                    Ensure(login.Error == ErrorCode.ERR_Success && login.IsPlayerActorReused && login.SessionEpoch > firstEpoch,
                        $"second login error={login.Error} epoch={login.SessionEpoch} reused={login.IsPlayerActorReused}");
                    await self.WaitForReplacementPush(beforePushCount, 2000);
                    int index = self.SessionReplacedReasons.Count - 1;
                    Ensure(self.SessionReplacedSessionIds[index] == oldSessionId,
                        $"replacement push received by session={self.SessionReplacedSessionIds[index]} expected={oldSessionId}");
                    Ensure(self.SessionReplacedErrors[index] == ErrorCode.ERR_OnlineAccountLoggedInElsewhere,
                        $"replacement error={self.SessionReplacedErrors[index]}");
                    Ensure(!string.IsNullOrWhiteSpace(self.SessionReplacedReasons[index]), "replacement reason is empty");
                    using G2C_Logout oldLogout = await first.Call(C2G_Logout.Create(), 800) as G2C_Logout;
                    Ensure(oldLogout.Error == ErrorCode.ERR_OnlineSessionNotLoggedIn, $"old session auth error={oldLogout.Error}");
                    await self.Root().GetComponent<TimerComponent>().WaitAsync(1200);
                    Ensure(first.IsDisposed, "old session was not closed within bounded replacement window");
                });

                failed += await self.Case("CLP-08", async () =>
                {
                    first?.Dispose();
                    using G2C_Ping ping = await second.Call(C2G_Ping.Create()) as G2C_Ping;
                    Ensure(ping.Error == ErrorCode.ERR_Success, $"new session ping error={ping.Error}");
                });

                failed += await self.Case("CLP-09", async () =>
                {
                    second.Dispose();
                    second = null;
                    await self.Root().GetComponent<TimerComponent>().WaitAsync(1000);
                    third = self.CreateSession();
                    using G2C_Login login = await third.Call(Login(user)) as G2C_Login;
                    Ensure(login.Error == ErrorCode.ERR_Success && login.IsPlayerActorReused && login.PlayerId == playerId,
                        $"grace login error={login.Error} player={login.PlayerId} reused={login.IsPlayerActorReused}");
                });

                failed += await self.Case("CLP-10", async () =>
                {
                    third.Dispose();
                    third = null;
                    await self.Root().GetComponent<TimerComponent>().WaitAsync(62000);
                });

                failed += await self.Case("CLP-11", async () =>
                {
                    fourth = self.CreateSession();
                    using G2C_Login login = await fourth.Call(Login(user)) as G2C_Login;
                    Ensure(login.Error == ErrorCode.ERR_Success && !login.IsPlayerActorReused && login.PlayerId == playerId,
                        $"post-timeout login error={login.Error} player={login.PlayerId} reused={login.IsPlayerActorReused}");
                });

                failed += await self.Case("CLP-12", async () =>
                {
                    using G2C_Logout logout = await fourth.Call(C2G_Logout.Create()) as G2C_Logout;
                    Ensure(logout.Error == ErrorCode.ERR_Success, $"logout error={logout.Error}");
                    using G2C_Logout secondLogout = await fourth.Call(C2G_Logout.Create()) as G2C_Logout;
                    Ensure(secondLogout.Error == ErrorCode.ERR_OnlineSessionNotLoggedIn, $"second logout error={secondLogout.Error}");
                });

                failed += await self.Case("CLP-13", async () =>
                {
                    using Session session = self.CreateSession();
                    using G2C_Logout logout = await session.Call(C2G_Logout.Create()) as G2C_Logout;
                    Ensure(logout.Error == ErrorCode.ERR_OnlineSessionNotLoggedIn, $"unauth logout error={logout.Error}");
                });
            }
            finally
            {
                first?.Dispose();
                second?.Dispose();
                third?.Dispose();
                fourth?.Dispose();
            }

            return failed;
        }

        private static async ETTask<int> RunRestartCheck(this OnlineTestComponent self)
        {
            return await self.Case("CLP-06", async () =>
            {
                using Session session = self.CreateSession();
                using G2C_Login login = await session.Call(Login(Options.Instance.OnlineTestUser)) as G2C_Login;
                Ensure(login.Error == ErrorCode.ERR_Success, $"restart login error={login.Error}");
                Ensure(login.AccountId == Options.Instance.OnlineTestExpectedAccountId, $"account changed {login.AccountId}");
                Ensure(login.PlayerId == Options.Instance.OnlineTestExpectedPlayerId, $"player changed {login.PlayerId}");
            });
        }

        private static async ETTask<int> RunReplaceQuick(this OnlineTestComponent self)
        {
            int failed = 0;
            string user = string.IsNullOrWhiteSpace(Options.Instance.OnlineTestUser)
                    ? $"adr{DateTime.UtcNow:HHmmss}{Random.Shared.Next(1000, 9999)}"
                    : Options.Instance.OnlineTestUser;

            using (Session registerSession = self.CreateSession())
            {
                using G2C_Register register = await registerSession.Call(Register(user)) as G2C_Register;
                Ensure(register.Error == ErrorCode.ERR_Success, $"setup register error={register.Error}");
            }

            Session first = null;
            Session second = null;
            long firstEpoch = 0;
            try
            {
                first = self.CreateSession();
                using (G2C_Login firstLogin = await first.Call(Login(user)) as G2C_Login)
                {
                    Ensure(firstLogin.Error == ErrorCode.ERR_Success, $"setup first login error={firstLogin.Error}");
                    firstEpoch = firstLogin.SessionEpoch;
                }

                failed += await self.Case("CLP-07", async () =>
                {
                    int beforePushCount = self.SessionReplacedReasons.Count;
                    long oldSessionId = first.Id;
                    second = self.CreateSession();
                    using G2C_Login secondLogin = await second.Call(Login(user)) as G2C_Login;
                    Ensure(secondLogin.Error == ErrorCode.ERR_Success && secondLogin.IsPlayerActorReused && secondLogin.SessionEpoch > firstEpoch,
                        $"second login error={secondLogin.Error} epoch={secondLogin.SessionEpoch} reused={secondLogin.IsPlayerActorReused}");
                    await self.WaitForReplacementPush(beforePushCount, 2000);
                    int index = self.SessionReplacedReasons.Count - 1;
                    Ensure(self.SessionReplacedSessionIds[index] == oldSessionId,
                        $"replacement push received by session={self.SessionReplacedSessionIds[index]} expected={oldSessionId}");
                    Ensure(self.SessionReplacedErrors[index] == ErrorCode.ERR_OnlineAccountLoggedInElsewhere,
                        $"replacement error={self.SessionReplacedErrors[index]}");
                    Ensure(!string.IsNullOrWhiteSpace(self.SessionReplacedReasons[index]), "replacement reason is empty");
                    using G2C_Logout oldLogout = await first.Call(C2G_Logout.Create(), 800) as G2C_Logout;
                    Ensure(oldLogout.Error == ErrorCode.ERR_OnlineSessionNotLoggedIn, $"old session auth error={oldLogout.Error}");
                    await self.Root().GetComponent<TimerComponent>().WaitAsync(1200);
                    Ensure(first.IsDisposed, "old session was not closed within bounded replacement window");
                });

                failed += await self.Case("CLP-08", async () =>
                {
                    using G2C_Ping ping = await second.Call(C2G_Ping.Create()) as G2C_Ping;
                    Ensure(ping.Error == ErrorCode.ERR_Success, $"new session ping error={ping.Error}");
                });
            }
            finally
            {
                first?.Dispose();
                second?.Dispose();
            }

            return failed;
        }

        private static async ETTask<int> RunLifecycleOperationId(this OnlineTestComponent self)
        {
            return await self.Case("LIFECYCLE-OPERATION-ID", async () =>
            {
                string first = OnlinePlayerActorSystem.BuildLifecycleOperationId(1001, 1, "save");
                string retry = OnlinePlayerActorSystem.BuildLifecycleOperationId(1001, 1, "save");
                string recreated = OnlinePlayerActorSystem.BuildLifecycleOperationId(1002, 1, "save");
                string logout = OnlinePlayerActorSystem.BuildLifecycleOperationId(1001, 1, "logout");

                Ensure(first == retry, "same lifecycle transaction did not preserve its operation id");
                Ensure(first != recreated, "recreated actor reused the previous profile-save operation id");
                Ensure(first != logout, "different lifecycle transaction types shared an operation id");
                Ensure(first.Length == 32 && IsLowerHex(first), $"operation id format invalid: {first}");
                await ETTask.CompletedTask;
            });
        }

        private static bool IsLowerHex(string value)
        {
            foreach (char c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        private static async ETTask<int> Case(this OnlineTestComponent self, string id, Func<ETTask> body)
        {
            long start = TimeInfo.Instance.ClientNow();
            try
            {
                await body();
                Console.WriteLine($"{id} | Passed | {TimeInfo.Instance.ClientNow() - start}ms |");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine($"{id} | Failed | {TimeInfo.Instance.ClientNow() - start}ms | {e.Message}");
                return 1;
            }
        }

        private static Session CreateSession(this OnlineTestComponent self)
        {
            IPEndPoint endPoint = NetworkHelper.ToIPEndPoint(Options.Instance.OnlineTestAddress);
            return self.Root().GetComponent<NetComponent>().Create(endPoint);
        }

        private static byte[] ReadStoreBytes(this OnlineTestComponent self)
        {
            string storePath = System.IO.Path.GetFullPath(Options.Instance.OnlineStore);
            if (!System.IO.File.Exists(storePath))
            {
                throw new Exception($"store file not found: {storePath}");
            }
            return System.IO.File.ReadAllBytes(storePath);
        }

        private static async ETTask WaitForReplacementPush(this OnlineTestComponent self, int beforeCount, int timeoutMs)
        {
            long end = TimeInfo.Instance.ClientNow() + timeoutMs;
            while (TimeInfo.Instance.ClientNow() < end)
            {
                if (self.SessionReplacedReasons.Count > beforeCount)
                {
                    return;
                }
                await self.Root().GetComponent<TimerComponent>().WaitAsync(50);
            }
            throw new Exception("old session did not receive replacement push");
        }

        private static async ETTask EnsureRegisterAndLogin(this OnlineTestComponent self, string userName, string password)
        {
            using Session session = self.CreateSession();
            using G2C_Register register = await session.Call(Register(userName, password)) as G2C_Register;
            Ensure(register.Error == ErrorCode.ERR_Success, $"boundary register user={userName} passwordLength={password.Length} error={register.Error}");
            using G2C_Login login = await session.Call(Login(userName, password)) as G2C_Login;
            Ensure(login.Error == ErrorCode.ERR_Success, $"boundary login user={userName} passwordLength={password.Length} error={login.Error}");
        }

        private static string UniqueUser(string prefix)
        {
            return $"{prefix}{Random.Shared.Next(100000, 999999)}";
        }

        private static C2G_Register Register(string userName, string password = Password)
        {
            C2G_Register request = C2G_Register.Create();
            request.UserName = userName;
            request.Password = password;
            return request;
        }

        private static C2G_Login Login(string userName, string password = Password)
        {
            C2G_Login request = C2G_Login.Create();
            request.UserName = userName;
            request.Password = password;
            return request;
        }

        private static void Ensure(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }
    }
}
