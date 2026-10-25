# 潜地怪与恶犬采用版正式接入

2026-10-25：本会话两种怪物的正式资源与单人／联机表现入口已接入并完成限定验证。用户先对Creatures v1最终两条动态回复“可以”采用，再对“现在是否也授权本会话继续正式接入这两种怪物？”回复“可以”授权正式开工。此项不包含其他两个会话的场景视觉。

[查看正式资源回放](index.html)（每种正常8秒、0.5倍慢放16秒）。正式主角保持原尺寸，同框展示下潜／原位扰动／抓地撑身／爬行／近战，以及四足跑动／锁向蓄势／扑咬／错相落地。这里是正式Controller消费手动安排快照的隔离回放，隐藏了环境和头顶HUD以便检查动作；不是自然对局、实际AI追击、判伤录像或真人双端实玩。

## 实际接入

- 采用源烘焙成两个正式Prefab，各3个持久Mesh；潜地怪10个Material、犬7个Material；每种9个正式动画片段。放在`Client/Assets/Game/Art/Enemies/Creatures`及`Client/Assets/Game/Prefabs/Characters/ArcadeBurrower/Hound.prefab`，保留meta。
- 沿既有`ArcadeObstacleVisualSet/Content`聚合加载句柄持有两种休眠嵌套模板；没有新增异步资源通道。场地就绪同时等待本关四足演员依赖；失败返回原加载错误入口，实例装配异常会回滚。
- `ArcadeCreatureView`没有Update、业务计时或位移积分，只采样正式片段。`RoundEnemyVisual`拥有实例，移除、换身份和切轮沿原根清理；持久共享Mesh不登记为运行时自有网格，不会被移除时销毁。
- 身体技能按权威年龄采样；步态按实际位置累计距离匹配，停步停止迈腿。犬口颈另取BiteFace片段叠加已有近战前摇／收招，即使贴身或被障碍挡住也不会丢失真实近战动作。
- 单人与联机共用`SetCreatureState(id, age, facing)`。单人年龄取ContentClock-BornAt；服务器取既有ContentElapsed和锁向，快照创建、克隆、协议编解码、客户端CopyBattle与投影均保留。协议源新增EnemyInfo第32—34字段，已通过`Tools/gen-protocol.ps1`生成双端。
- 原配置7.5秒周期、1.2秒预警及地下原位／免伤、锁向冲进、普通近战、人数／伤害／单双人规则不变。采用展示时间轴适配真实阶段，未反改规则匹配原9.6／8秒演示时长。

## 验证

| 范围 | 最终结果 | 证据 |
| --- | --- | --- |
| CreaturePresentationTests | 14/14 | [client-green.json](client-green.json) |
| ArcadeRoundContentViewTests | 7/7 | [regression.json](regression.json) |
| 原潜地伤害窗口、特殊怪出生预算两个完整方法 | 2/2 | [regression.json](regression.json) |
| CreatureOnline快照创建、克隆、Codec、真实地下弹丸免伤 | 4/4 | [server-checks.txt](server-checks.txt) |
| 四条MP4逐帧完整解码 | 各192帧 | [video-verification.json](video-verification.json) |
| 正式片段与采用源局部姿态 | 最大位置误差0.001543米、角度0.172459度 | [capture.txt](capture.txt) |
| 重复烘焙临时AnimationClip数量 | 0→0→0 | [bake-repeat.json](bake-repeat.json) |
| 3230个原正式文件保护 | 3229未变，仅聚合模板为授权变更 | [resource-protection.json](resource-protection.json) |

所有测试使用真实非空短类或完整方法，includePassingTests=true。先运行原版本12项RED（其中清理检查因旧演员缺失而空引用，其余缺正式资源／状态入口），实施后12项GREEN；审查增加依赖顺序检查并扩展犬近战，共3例中2例RED／1例原通过，修复后最终14/14。邻接7+2完成。Unity累计执行50次用例，服务端4次，未超过计划80次；一次参数化完整方法筛选未命中，随后先查询真实方法，再用等值RQL只选同一方法的两个参数例，没有回退整套。

没有运行完整套件、Player Build或资源打包。服务器编译最初出现ContentElapsed double→float需要显式转换，已修正并以真实新编译进程完成4项检查。Unity首次烘焙发生在新类型尚未导入前，刷新源码后已实际编译执行成功；历史Console保留该已解决的动态编译错误，不冒称历史Console为空。

## 独立审查及修正

请求requesting-code-review独立只读审查，发现两项P2及一项P3：

1. 聚合演员模板晚于场地时会提前创建holder并抛异常。已先复现就绪断言失败，再把依赖纳入就绪门槛并对实例装配回滚；模板空、残缺、恢复后当前年龄与位置均由真实测试覆盖。
2. 犬技能身体姿态覆盖了真实近战口部。已先复现犬前摇改变时下颌角度差0，再拆分BiteFace叠加，保留身体技能姿态及原判伤；两种近战参数例最终通过。
3. 重复烘焙复制动画后未释放临时源片段。已在CopySerialized成功后销毁非持久源动画，两次重跑计数0/0/0。

审查排除项已核实：上层轮代次／Revision拒绝旧快照，演员年龄也防倒播；暂停不积分；共享Mesh不在运行时释放名单；锁向不使用本机玩家位置；未改变伤害、人数和位移。没有将首次审查后修复与测试复验称为第二轮独立复审。

## 收尾与交接

按原明确分工，总清单保持只读，本会话没有git add、commit、push；当前工作区及其他会话修改保留。正式实施计划在`Docs/Plans/2026-10-25-burrower-hound-applied.md`；`task-manifest.json`列出本项建议审查／选择性提交路径和哈希，排除逐帧PNG及工具bin/obj。总会话可依据本项授权、23+4项检查、回放与审查修正记录汇总并提交推送，不能把其他场景候选混为已采用。

Unity最终工程／场景／编译及临时对象检查见`editor-final.json`。所有MCP调用均遵守本批原子独占锁，拍摄和烘焙在additive临时场景中try/finally恢复；Boot不保存，自持锁已释放。
