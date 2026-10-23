# 积分三选一商店实施计划

**Spec:** `Docs/superpowers/specs/2026-10-23-score-shop-design.md`
**Goal:** 每整关结束各自用积分刷新／购买一个现有空投，下一关开场生效。
**Execution:** 当前工作区内顺序实施；用户禁止默认worktree且已授权当前分支自动中文提交推送。全部必要验证通过前不提交半成品。

## 全局约束

- v2视觉与3000／4000／5000／6000价格已采用，刷新固定500。
- 不打开旧SlayerMart现金门禁；不新增道具效果；不改其他会话的武器进化、地图或敌人。
- 只通过Config/gen.bat和Tools/gen-protocol.ps1生成配置、协议；UI通过正式生成器。
- 先写有行为断言的定向测试，观察失败，再实现。仅EditMode指定真实testClass／testMethod与最大数量，includePassingTests=true；服务端工具同样精确选择检查名，不跑完整套件／Player Build。

## Task 1: 共享商品配置及积分事务规则

Files: `Config/Defines/{beans,tables}.xml`, `Config/DataCsv/arcade_score_shop.csv`, `Utility/Arcade/ArcadeScoreShopRules.cs`, `Utility/Arcade/ArcadeTreasureRoomRules.cs`, `Utility/Config/{RuntimeConfigDefinitions,LubanRuntimeConfigProvider}.cs`, server Config和共享编译链接。

接口：不可变配置只关联正式Drop和价格；可克隆访问状态保存关卡、版本、三个DropId、完成及待交付Drop；纯事务返回候选与扣分，失败不修改输入。积分状态增加已处理加命档位，消费不变更倍率。

1. 写`ScoreShopRulesTests`覆盖关卡资格、三项唯一、500刷新至少一项变化、足额购买结束、跳过、余额不足与旧版本请求无副作用；`ScoreShopLifeTests`覆盖消费后重跨档、下一档及满命不排队。
2. 运行以上两个真实类，各上限16，includePassingTests=true。Expected: 缺失商店规则／消费后重复奖励的明确失败。
3. 实现纯规则与权威配置，生成双端；重新运行同范围。Expected: 全部通过。

## Task 2: 单人安全节点、持久化与下一关交付

Files: `Model/Arcade/{ArcadeRunContracts,ArcadeRunModel}.cs`, `Utility/Save/ArcadeRunFileStore.cs`, `System/Arcade/ArcadeRunSystem.ScoreShop.cs`, RunSystem主流程、Combat正式Drop交付、计分与检查点调用。

接口：整关完成创建一次访问；事务先保存再提交Model；未完成不得继续；首关与终局不开放；购买的Drop通过ArenaEntryState或轮级节点持久化后在开场恢复，避免加载消耗或重入重复领取。高水位字段随个人积分同样保存／恢复。

1. 写`ScoreShopRunTests`真实文件保存回读、重复请求、恢复选项、购买交付、存盘失败不扣款、终局不触发。
2. 指定该类上限16运行RED；实现后GREEN。另执行受影响积分／恢复类，逐类声明真实名称和上限。

## Task 3: 联机个人权威事务

Files: OnlineArcadeRunComponent、OnlineArcadeRunSystem.ScoreShop、计分／Ready／关卡完成／Drop交付、客户端OnlineArcadeRunSystem.ScoreShop、OnlineArcadeRunContracts、Config/Proto/OnlineOuter及Inner对应消息、路由Handler。

接口：服务器保存每人商店访问和高水位；请求含操作Id、访问版本与商品Id，回复最新本人快照；多人全部完成后允许Ready；不允许他人积分或掉落，消息重试不重扣。协议经正式脚本生成。

1. 增加`ScoreShopOnlineChecks`限定最多16个真实检查；运行RED、实现并GREEN。
2. 精确运行受影响计分、普通切关和旧商店关闭检查；不假称真人双端验收。

## Task 4: 采用版正式UI与入口

Files: 新`ArcadeScoreShop`独立UI、`ArcadeScoreShopOfferBox`外部Box Prefab及生成View；手写UICtrl；Resume入口、语言权威源与生成器。

接口：Dialog层阻挡战斗输入；CustomListView直接引用外部Box；Controller只显示System快照及发请求；固定布局在Prefab，按钮随余额／未完成状态刷新；失败可重试；隐藏释放焦点与监听。

1. 定向`ScoreShopUiTests`最多12，检查Unity反序列化后的ReferenceCollector、生成View、外部Box、所有引用、语言键与入口状态。先RED。
2. 用Editor authoring工具制作采用版Prefab、运行UI与Box生成器，接入正式Controller，GREEN。
3. Unity实际加载截图核对已采用三卡布局，验证刷新、购买／跳过、低余额和重新显示，无额外视觉设计。

## Task 5: 审查、验证与交付

- 更新执行总清单和证据，区分离线平衡模型、EditMode、服务端检查及实玩。
- 最终用requesting-code-review执行一次独立只读审查；关注跨关计时、保存失败、协议重复和life高水位旧档兼容。
- 修复重要发现并重跑对应限定检查，确认没有未解决问题。
- 只暂存本任务文件／hunk，中文提交并推送当前分支origin；其他会话工作保留。

## Review Focus

旧档无字段迁移、非海岛普通关序号与配置ID区别、断线恢复与单人存盘失败、商店未完成绕过Ready／挑战、同枪升档／无人机领取幂等、末关及同轮恢复不重开商店。
