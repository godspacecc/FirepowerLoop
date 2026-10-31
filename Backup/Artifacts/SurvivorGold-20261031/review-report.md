# 幸存者金币独立审查

审查范围：计划、implementation-report.md、任务专属review.patch、changes.json，以及当前存档、金币System、入口Controller、UIHelper/BaseUICtrl、开局异步生命周期与测试。仅写本报告，未改生产资源，未运行Unity测试或构建。

## Spec结论

代码层面符合已确认范围：取消新局击杀积分，每份金币1；拾取同步原子落盘成功后才移除；独立余额不兑换旧分数；新局保留余额，结束结果仅记录本局金币，不再次入账；入口先读余额，坏档禁用开始；无局外消费实现。未发现需阻止合入的确定性业务错误。

## Quality结论

有条件通过静态审查，完整验证结论仍待以下证据补齐。存档活动局令牌与连续领取水位使重复领取幂等、有界存储，旧局在新局开始后被拒绝；Stop清空运行令牌与地面金币，无金币异步写入链。入口Show同步完成后Configure，OnShow/OnHide清回调，开始先清回调再调用既有带generation的异步开局；重复点击有现成startingRun门禁。语言变更刷新余额与错误，固定文案由LocalizedText负责。

尚未证实的关键边界：
- Client/Assets/Tests/EditMode/SurvivorRuntimeTests.cs:41 新增测试仅覆盖正常拾取、第二次Tick及重开余额；并未覆盖拾取落盘失败时地面金币保留、冷却后重试只加1。SurvivorGoldStoreTests.cs:9 的不可写测试发生在TryBeginRun，不能替代已经开局后的TryCollect失败验证。建议补一个真实文件失败后的恢复测试，断言地面集合、视图RemoveDrop、余额与RunGold。
- Client/Assets/Tests/EditMode/SurvivorUiTests.cs:13 是Prefab引用检查，不能当作入口关闭/重复点击/坏档门禁的交互验证。代码看起来正确，但当前报告未提供这些交互的实际证据。
- source-collection-final.json确证入口地址startup;arcade及金币地址arcade，LootCtrl.cs:241遍历catalog.Drops预加载，实际地址链吻合。源收集成立；当前运行真实加载、下载和发布环境仍未由本审查证实。不可把源检查或Prefab编辑器渲染当运行验收。

核读storage-final.json为3/3、entry-final.json为1/1；报告记录runtime-green为38/38、UI原9/10再针对失败项修复1/1。本审查没有重复执行测试。正式入口图为Prefab编辑器渲染而非游戏运行实拍。发布包未检查、未构建。

提交边界：本次运行时代码依赖共享工作区现有幸存者地图、枪械、经验等实现，需以任务基线增量暂存；两份Localization输出无独立基线，必须只暂存对应金币文案差异。其他任务差异未纳入问题清单。

## 补证复审（最终结论）

只复审上列三项缺口，没有扩展范围或重跑测试。

1. GoldWriteFailureKeepsDropUntilCooldownRetrySucceeds 使用真实只读钱包文件先确认TryCollect失败，再验证TickGold不移除地面金币、不增加余额或本局金币、不改旧文件；恢复可写后验证冷却期内不领取、到期仅移除一次并落盘1金币。write-failure.json 对应真实方法1/1通过，关闭保存失败恢复缺口。
2. SurvivorGoldEntryInteractionTests 使用正式Prefab和Controller及临时钱包，覆盖关闭后旧回调失效、重复开始仅执行一次、坏档禁用按钮且显示错误、返回后修复钱包重开、语言事件刷新。entry-interactions.json 对应真实方法1/1通过，关闭此前入口交互缺口。Prefab资源读取为该交互测试的明确边界，未用于代替真实资源加载证明。
3. runtime-resources.json 记录实际入口与金币地址均成功加载；结合实现者提供的正常Play、DefaultPackage Succeed、LoadAssetSync及finally.Release执行说明，当前编辑器运行环境加载缺口关闭。发布包仍未验证，未打包；此边界不阻塞已授权源码交付。

最终Spec：通过。最终Quality：通过，无Critical或Important待修项。以上结论限本次任务增量及已给出的实际证据；不宣称完整实战或发布包验收。
