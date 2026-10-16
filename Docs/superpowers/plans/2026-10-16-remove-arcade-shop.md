# 街机商店清理实施计划

> 使用 executing-plans 在当前会话内执行；用户已要求“直接改吧”。

**Goal:** 移除单人、双人商店业务，保留恢复、结算、准备与强化道具。
**Architecture:** 安全节点独立于商品；复用 ArcadeResume UI 和 OnlineRoom UI，商品数据不再进入运行态或协议。旧局效果保留，不提供新购买入口。
**Tech Stack:** Unity C#、QFramework、ET、Luban。
**Spec:** `Docs/superpowers/specs/2026-10-16-remove-arcade-shop-design.md`

## 全局约束

- 当前目录执行，不创建 worktree；不提交、不推送，不打包。
- 保留资源与 .meta，生成代码只通过正式生成器更新。
- 不读取另一项目数据，不改强化道具参数。
- Unity MCP 当前不可用，不能宣称 Editor、Prefab 反序列化或实玩通过。

## Review Focus

- 旧节点含商品报价：丢弃报价后仍可恢复生命、地图、分数及效果。
- 单人结果确认失败：保留节点和重试入口，地图释放不丢结果。
- 双人零生命成员：不得再要求买命，存活成员准备后可继续。
- 双人旧购买 action 数值：拒绝且不改变资源；准备/结算数值保持稳定。
- 换图强化状态：时长、次数、锚点及同类刷新逻辑不变。

## Task 1: 契约与单人业务

- [x] 添加定向清理检查，确认原购买接口和商品配置使其失败。
- [x] `ArcadeRunContracts.cs` 将 Shop 改为 Checkpoint，使用旧 JSON 字段的单向兼容读取；移除报价类型，阶段改为 SafeCheckpoint 并保持数值。
- [x] `ArcadeRunSystem.cs` 删除生成商品、购买、类型映射；`ArcadeRunFileStore.cs` 验证节点而非商品。
- [x] 所有生产调用改为 Checkpoint，不用报价是否为空决定流程。

## Task 2: 双人业务与 UI

- [x] `OnlineArcadeRunSystem.cs`（双端）、`OnlineArcadeRunComponent.cs`、`OnlineArcadeRunContracts.cs` 删除商品、购买、转账与补售生命；安全操作改为 Checkpoint，保留 ready/挑战/结算/放弃数值。
- [x] `ArcadeResumeUICtrl.cs` 复用现有字段支持结果、挑战及双人确认；`OnlineRoomUICtrl.cs` 保留普通准备流程。
- [x] 删除手写 `ArcadeShopUICtrl.cs` 交易控制器，保留旧 Prefab、View 与 .meta。
- [x] 协议权威源删除报价及交易参数，保留必要消息 opcode，执行 `Tools/gen-protocol.ps1`。

## Task 3: 配置与验证

- [x] `Config/Defines` 以 `ArcadeSupportVisualConfig` 替换商品表，只保留旧局支援视觉的身份、地址和偏移；运行时目录改为 SupportVisuals。
- [x] 删除商品源和交易枚举；清理 ShopOfferCount，种子盐改为 EncounterSeedSalt，运行 `Config/gen.bat`。
- [x] 更新直接受影响的测试与检查夹具。对旧接口的测试改验拒绝交易及必要安全操作。
- [x] 服务端编译、客户端可用编译、定向检查与残留扫描；记录无法执行的 Unity 验证。

## 执行记录

- 2026-10-16：用户授权当前会话直接实施，覆盖技能中的再次计划审批要求；使用当前工作区，保留已有 DLL、字体和 ProjectSettings 改动。
- 三项任务均已完成源码实施、正式生成、残留扫描与可用独立验证。28 项定向检查通过；客户端/测试源码及服务器编译通过。Unity MCP 不可用，编辑器及双客户端实玩留待连接后验证，详见 `Docs/Plans/街机商店清理验证-20261016.md`。
