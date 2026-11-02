# 粘液瓶与强化清理最终独立只读审查

审查日期：2026-11-02。基线 HEAD：05f93c776557cb209a1fe6585e407ab9c5a07f3c。

结论：在本次授权范围内，未发现可确认的 P0/P1/P2/P3 问题。可继续本次提交收尾。此结论来自当前工作文件审查，不是新的测试或整局实测结果。

## 方法与范围

按 requesting-code-review 及 code-reviewer 模板独立审查；本人就是被分派的只读 reviewer，不再次委派。读取 implementation-report.md、当前正式文件和 git show HEAD 原文；Elements 等文件存在默认 index D/工作文件仍在的情况，已直接用磁盘文件和 HEAD 原文逐段比较，未把 index 的删除状态误判为实现删除。未调用 Unity、未运行测试、未修改正式文件、index、HEAD 或分支。

## 已核对

- SlimeBottle 独立配置身份、五级参数、固定5秒冷却、0伤害、3秒区域；独立飞行/区域预算，不调用伤害与掉落结算。投出冻结数值，区域强度由当前权威升级定义求值并冻结。
- 区域判断实时读取敌人位置，区域间取最大值，离开即恢复；Elements 将冰霜和粘液取最大值，冰霜跨到期时分段计算，不修改敌人基础 Speed。TickEnemies 的普通追踪、配置近战和特殊敌人的通常追踪消费同一个 moveSeconds。
- 暂停、UI阻挡、未就绪都在主 Tick 门槛阻断；Stop 清空粘液在途/区域与冷却；实例表现只由年龄 Sample，无自主Update/粒子计时。结束与复用不残留旧领域。
- Throwable 两种武器共用 ExclusiveGroup，CanChooseRoute 同时用于候选、选择与刷新；RifleTip 单独分组，不会被投掷互斥误封。模型共享投掷HUD冷却字段在当前 TickMolotov 后 TickSlimeBottle 的顺序下由已装备粘液瓶覆盖，不污染燃烧瓶冷却。
- Content 在 Released、generation、expectedRoot 验证后绑定粘液模板；IsSurvivorMolotovReady 合入粘液就绪，原场景入口/ConfirmBattlePrepared/运行失效检查均因此覆盖新增模板。模板失败沿同一个 Content catch，清双方投掷池；ReleaseContent 同样先清表现再释放资源句柄。晚回调护栏未被削弱。
- 瓶/区域活动池与闲置池均有限并清理；区域回收先 ResetVisual；正式 SlimeZone 绑定 Pool 与12滴；嵌套资源由 Content 主地址依赖提供，没有复制加载服务或新增动态寻址。
- HUD绿色瓶图按授权复用 MolotovIcon 并 tint，复用时还原其他图标白色，沿既有投掷冷却环；空槽覆盖图标和进度。
- 主武器初始 Rifle.1 保留，删除 Rifle.2–5 和六项Weight0候选；当前配置五级粘液参数完整。稳定Kind标题替代行号，真实基础速度与原累计加成用于米/秒文案。搜索正式 Game 代码与 Config 未发现退休数字键的活动消费者或 GetUpgradeName 残留；数字键只留在退休清单。
- 已读取 real-address.txt、real-collection.txt、boot-play-real-load.txt，与当前请求链一致：公共视觉精确地址唯一、arcade标签，正式Prefab/3mat/mesh在依赖报告中；Boot已有startup+arcade下载标签；报告记载真实YooAssetResourceLoader从正常Boot Play加载成功。

## 测试与证据边界

本轮没有重跑测试。实施报告提供33个唯一runner例通过及相应原始结果范围，表现测试新增覆盖真实收集依赖与有界池；这些是实施阶段证据。代码阅读确认相关断言针对真实当前配置和方法，而不是仅靠模拟资源存在断言收集成功。

本轮没有重新播放正式GIF或长时完整幸存者战斗，没有触发资源构建、Player Build、发布清单刷新或发布。现有运行环境真实加载证据可以支持当前环境，不说明旧发布包/用户安装包已经更新。

## 考虑后未纳入本次裁决

- 同工作树其他聊天的附魔、尸潮、特殊敌人及设置视觉改动：超出实施文件清单；阅读涉及本次减速消费的交界处，但不对其他任务整体实现作验收。
- 第三方 YooAsset 收集器或框架重构：本任务沿现有链，无修改必要。
- 区域出生/到期与敌人移动在离散帧中的先后：当前主循环最大步长0.05秒，未作为本次独立缺陷上报；用户确认的是3秒权威区域与离开位置判定，没有要求跨区域边界的连续积分碰撞求解。
