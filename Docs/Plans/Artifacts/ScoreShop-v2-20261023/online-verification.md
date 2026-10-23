# 联机积分商店验证记录

日期：2026-10-23。范围：服务端权威事务、消息链、客户端Online adapter；不代表真人双端验收。

## 已实现

- 新正式休闲局创建 `ScoreShopRulesVersion=1`，旧档缺字段为0、旧顺序模式不追加商店。整关完成为双方分别创建三项；终局不开放；小轮流程不开放。
- 每人积分、商店候选/随机种子/版本、完成及待交付Drop一起保存；动作10购买、11刷新、12跳过沿现有安全节点串行事务。保留旧动作四段fingerprint，新动作追加DropId；动作1/2继续拒绝。
- 普通Ready、挑战Ready、PrepareBattle及自动切关均受未完成商店门禁保护。
- 下一普通关候选通过同一ApplyDrop入口领取；Delivered与完整轮前角色状态同次保存。保存失败不发布现场、不消费待交付标记；加载等待不烧购买时长，恢复轮节点不再发满时长。
- ScoreLifeTier随所有服务端计分和旧商店benefit保存；消费前迁移旧档高水位，重跨20万不重复加命，40万正常加命。
- 正式三跳请求传递DropId，快照携带各人ScoreShopJson；客户端只读取本人并返回clone。网络不确定请求保留OperationId，支持同意图重试；临时断线保留未知请求，换账号/对局或明确结束清理。
- 网络能力指纹追加`|score-shop.v1`，存档ConfigFingerprint不变。所有协议通过Tools/gen-protocol.ps1生成。

## 验证证据

1. 初始新增 `ScoreShopOnline` 14项定向检查：
   `dotnet run --project Tools/ArcadeFullRouteOnlineChecks -- testClass=ScoreShopOnline maxTests=16 includePassingTests=true`
   RED：0/14，缺少个人持久化、OpenScoreShops/交易/交付分支及新fingerprint形状。随后实现并扩展16项，GREEN。
2. 兼容防线新增回归先跑相同真实类，RED 15/16：`Missing explicit score shop compatibility version`。补版本门禁后16/16。
3. 最后完整编译（不含Player Build）：
   `dotnet build Tools/ArcadeFullRouteOnlineChecks/ArcadeFullRouteOnlineChecks.csproj -t:Rebuild --nologo`
   退出0，0警告、0错误。补测试后工具自身Rebuild（`-p:BuildProjectReferences=false`）亦0警告、0错误。
4. 最终实际测试命令均采用已编译DLL，不依赖dotnet run增量缓存：
   - `dotnet Tools/ArcadeFullRouteOnlineChecks/bin/Debug/net8.0/ArcadeFullRouteOnlineChecks.dll testClass=ScoreShopOnline maxTests=16 includePassingTests=true`：16/16。
   - 同路径 `testClass=SlayerMartProtocolGate maxTests=10 includePassingTests=true`：10/10。
   - 同路径 `testClass=RoundRewardOnline maxTests=7 includePassingTests=true`：7/7。
   - 同路径 `testClass=SlayerMartReleaseGateOnline maxTests=6 includePassingTests=true`：6/6。
   - 同路径 `testClass=NextRouteOnline maxTests=10 includePassingTests=true`：10/10。原9项上限被拒绝，因为并发会话新增了SnapshotVersion第10项；核对真实数组后仅重跑此10项类，未回退完整套件。
5. `git diff --check -- <本次修改的协议/Online/计分/测试文件>`退出0。全局检查另见他人`FixedCoastSand.mat`三处行尾空白，未修改该资源。

已覆盖实际磁盘锁保存失败、管理器注入保存失败、首次消费与重放、参数冲突和旧购物动作拒绝、个人积分隔离、两人Ready/挑战防线、喷火器20秒/无人机25秒领取、加载冻结、恢复7秒不重置、交付保存失败保持旧Actor、生命高水位、快照与MemoryPack请求编解码。

## 本子任务修改文件（共享文件只改相应hunk）

新增：
- `Server/GameServer/Runtime/Hotfix/Server/Online/Arcade/OnlineArcadeRunSystem.ScoreShop.cs`
- `Client/Assets/Game/Scripts/System/Online/OnlineArcadeRunSystem.ScoreShop.cs`及`.meta`
- `Tools/ArcadeFullRouteOnlineChecks/ScoreShopOnlineChecks.cs`

手写修改：
- `Server/GameServer/Runtime/Model/Server/Online/Arcade/OnlineArcadeRunComponent.cs`
- `Server/GameServer/Runtime/Hotfix/Server/Online/Arcade/OnlineArcadeRunSystem.cs`
- `Server/GameServer/Runtime/Hotfix/Server/Online/Arcade/OnlineArcadeRunSystem.Island.cs`
- `Server/GameServer/Runtime/Hotfix/Server/Online/Arcade/OnlineArcadeRunSystem.Treasure.cs`
- `Server/GameServer/Runtime/Hotfix/Server/Online/Arcade/OnlineArcadeRunSystem.SlayerMart.cs`
- `Server/GameServer/Runtime/Hotfix/Server/Online/Arcade/OnlineArcadeHandlers.cs`
- `Server/GameServer/Runtime/Hotfix/Server/Online/Gate/C2G_ArcadeHandlers.cs`
- `Server/GameServer/Runtime/Hotfix/Server/Online/Player/G2P_ArcadeHandlers.cs`
- `Server/GameServer/Runtime/Hotfix/Server/Online/Player/OnlinePlayerActorSystem.cs`
- `Client/Assets/Game/Scripts/System/Online/OnlineArcadeRunSystem.cs`
- `Client/Assets/Game/Scripts/Utility/Arcade/ArcadeSlayerMartConfiguration.cs`（只追加网络能力标签）
- `Tools/ArcadeFullRouteOnlineChecks/Program.cs`（只加ScoreShopOnline严格分支）
- `Tools/ArcadeFullRouteOnlineChecks/SlayerMartProtocolGateChecks.cs`（只更新当前能力期望）

协议权威源与生成输出：
- `Config/Proto/OnlineOuter_C_12001.proto`（PlayerInfo.ScoreShopJson22，SafeCheckpoint.ScoreShopDropId9）
- `Config/Proto/OnlineInner_S_22001.proto`（G2P.ScoreShopDropId10，P2R.ScoreShopDropId9）
- `Client/Assets/Game/Scripts/Network/Generated/OnlineOuter_C_12001.cs`
- `Server/GameServer/Runtime/Model/Generated/Message/OnlineOuter_C_12001.cs`
- `Server/GameServer/Runtime/Model/Generated/Message/OnlineInner_S_22001.cs`

生成时保留其他会话已有字段（无人机、武器进化及CasualRouteVersion）。最后一次Rebuild发现另一会话刚添加CasualRouteVersion源字段但未生成而报3个CS1061，再运行正式协议脚本后已编译并通过上述限定检查。

## 客户端UI可用契约

- `GetScoreShopState()`：本人账本clone。
- `OperateScoreShopAsync(ScoreShopAction action,string dropId)`：Task<OnlineArcadeOperationResult>。
- `HasPendingScoreShopOperation`、`PendingScoreShopAction`、`PendingScoreShopDropId`：未知请求状态。
- `RetryPendingScoreShopAsync()`：只复用原请求，不新建意图。

## 未执行与交付边界

未调用Unity MCP刷新或测试，未运行完整套件，未创建worktree，未Player Build，未提交或推送。Unity编译/定向测试、实机UI截图与真人双端验收由主agent统一处理。初始Console有历史错误，不宣称Console清洁。本文件不把其他会话地图/武器改动列为本子任务交付。
