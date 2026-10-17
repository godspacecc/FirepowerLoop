# 全路线休闲普通轮总量补齐实施计划

> 当前会话主代理按executing-plans逐项执行；用户已授权持续实施、分段中文提交推送，不等待重复确认。

目标：补齐后期普通轮队列，保留独立Boss与既有战斗预算。设计：[有界行为](../specs/2026-10-17-casual-population-design.md)。技术：Unity C#、QFramework、双端同源ArcadeOpeningRules、Luban正式生成。

## 全局约束

- 自然观察结束并退出Play前不改Client、配置或生成输出；不因热编译破坏同局证据。
- 不打包／Player Build，不默认worktree，不修改正式玩家档，不读取AnotherDay数据。
- 只改普通轮总量和旧Boss占位组选择；奖励、休整、武器、生成间隔、同屏上限及独立Boss不一并调整。
- 指定真实短类、执行上限及includePassingTests=true；先有效红后绿，无全套回退。
- 配置仅改Config，经gen.bat双端生成；保留meta，只提交本段拥有路径。

## 任务1：双端真实队列失败证据

- [ ] 新建CasualPopulationFlowTests，9个普通Test：累计第9轮、相同场序不同主题、第30轮封顶、第40轮、旧Boss组改取普通组、权重精确合计、旧顺序、挑战及最终Boss隔离。
- [ ] 使用正式生成Tables与真实BeginWave、临时RunModel及运行态，不用另写公式替身证明生产入口。
- [ ] 服务端CasualPopulationOnline独立过滤最多9项：单／双人数、累计轮、封顶、占位Boss组、真实生成倍率与上限、旧模式／挑战／独立Boss隔离。夹具错误另记，不计功能红。
- [ ] 两个范围先取得有效失败，保存名称、预期和实际，确认不是编译／准备失败。

## 任务2：普通轮准备与一致的生成来源

- [ ] 客户端BeginWave按休闲普通轮决定总量；保留普通组，旧Boss占位取本场前序最近有效普通组，失败拒绝。group字段与后续生成、倍率和同屏预算一致。
- [ ] 服务端ResolveBattleGroup和PrepareWave采用同样选择及总量；TrySpawnEnemy再次解析须取得同组，不能普通队列套Boss倍率或MaxAlive=1。
- [ ] 不扩大UsesEarlyRounds：金币、掉落、财宝、休整范围保持；独立Boss和增援走原路径。
- [ ] 更新arcade_rule.csv正式指纹，经唯一gen.bat生成；旧在线节点不静默迁移，单人地图版本／轮级身份不变。
- [ ] 任务1通过后，回归受影响FullRouteContentFlowTests最多11项、FullRouteOnline最多28项；如类已有新增先核对数量再声明，无回退。

## 任务3：证据与交付

- [ ] 新鲜只读评审：Boss膨胀、场序混淆、服务端二次取组、特殊阶段误扩、指纹与旧节点。
- [ ] 双端配置bytes一致、定向diff无误，记录红绿及失败修正，release.enabled仍0。
- [ ] 更新差距清单及当前功能资料；只勾普通队列接线，实际节奏／平衡待验，提交推送。
- [ ] 继续自然流程与后续P1-2设计，不把定向通过当40轮自然通关。

## 执行记录

2026-10-17：本计划准备时自然新局仍在Play，未修改产品代码；静态路线证据已提交195abf8。
