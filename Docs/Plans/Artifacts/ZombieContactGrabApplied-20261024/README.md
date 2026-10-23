# 丧尸贴身抓挠正式接入验证

用户于2026-10-24对[贴身候选v4](../ZombieContactGrab-v4-20261023/README.md)回复“可以”，采用0.9米贴身起手、0.4秒前摇及大幅空手后拉/前抓/内下撕。现正式接入单人与现有联机路径。

[可播放页面](index.html) · [普通丧尸](shambler-claw.gif) · [疾行丧尸](gaunt-claw.gif) · [重型丧尸](bloated-claw.gif)

- 原尺寸正式丧尸与主角同框；三类AttackRange由1.35改0.9米，身体/弹丸碰撞半径不改。
- 真实出生读取生成配置，真实TickEnemies负责追近、停步、0.4秒前摇、伤害100→90和逃离取消；正式EnemyView.SetMeleeAttack/LateTick负责骨骼表现，没有临时动作函数替代。
- 每段100帧、20fps、5秒，首段命中，后半段跑开取消；为观察一次交互，首击收招结束后保持画面。接触点为手指到主角真实网格三角形的最近距离，数值及逐帧生命见timeline.csv和verification.json。
- 这是隔离渲染的正式规则/表现验证，不是完整自然闯关或真人双机联网录像。角色没有缩放或拉长骨骼，未制作撕咬；无武器。

双端使用0.55秒权威收招，保留原攻击间隔及玩家受伤无敌。离开取消后重新靠近等待完整前摇；群怪分离不推动正在攻击的丧尸。牛撞、潜地、现身/换位、死亡和回池清掉旧阶段。墓穴等复用正式丧尸的专用演员也沿同一入口采样，保留现身特效。联机快照包含前摇时长、当前剩余量和收招剩余量，面向权威目标；首次、重复和晚加载直接采样当前姿态。

## 验证

- 定向范围及命令见[实施计划](../../2026-10-24-zombie-contact-grab.md)，无完整测试套件或Player Build。
- 客户端ArcadeEnemyDropTests：58/58通过，含新增群怪挤压回归；新增距离和挤压用例先失败后通过。先前57项中的一次并行场景日志干扰已重新执行通过。
- ArcadeZombieGrabVisualTests：8/8通过；NextStageViewTests：12/12通过，包含墓穴权威抓挠及旧Attack不补播；允许待机呼吸带来的毫米级手部微动。客户端合计78项通过。
- 服务端MeleeWindupOnline：14/14通过，含目标切换、配置距离、停步、追上重前摇、收招、快照克隆/协议往返、群怪挤压、真实暗影首次击倒换位清收招。
- 独立只读审查发现群怪推动及换位残留两项，已修正并复核通过；没有未解决的重要问题。

生成入口：Config/gen.bat（客户端36表/服务端30表），Tools/gen-protocol.ps1。录制工具：Tools/Unity/ZombieContactGrabAppliedCapture.cs；编码工具：Tools/Unity/encode-zombie-contact-grab-applied.py（需要Pillow）。
