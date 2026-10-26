# 终局小龙正式接入计划

> 实施使用 executing-plans 技能，按下列检查项执行；用户已通过“那就替换”授权，不重复等待技术方案审批。

**目标：** 把已采用 FinalDragonModel-v1-20261026 模型与动作接入终局 Boss，保持原玩法。

**职责：** Tools/Unity 烘焙持久网格、材质、局部关节曲线；ArcadeObstacleVisualSet 的 Content 模板拥有资源；ArcadeCombatViewController 创建/销毁地图内演员，View 只采样；单人与联机 System 提供已有权威阶段、时间和朝向。Server 仅在现有朝向字段投影 Boss 锁定方向。不改协议、配置、伤害或保存。

**技术：** Unity C#、AnimationClip、既有 Unity MCP、限定 EditMode 测试。

**依据：** [总清单](执行总清单.md)、[已采用预览](../../Backup/Artifacts/FinalDragonModel-v1-20261026/index.html)。

- [x] FinalDragonPresentationTests 先运行失败验证：正式资源、根位移隔离、状态采样、模板缺失、生命周期。
- [x] FinalDragonBake 保存 ArcadeFinalDragon.prefab 与正式美术；用既有聚合模板加载，保留旧资源及 meta。
- [x] 正式 View 按原技能阶段采样，实际位移驱动步态，权威时间驱动呼吸。同步单人和联机，暂停不自行推进。
- [x] 限定小龙、四足、轮级View及终局规则测试；正式/候选回读比较与正式同框画面（8片段626点偏差0）；不 Player Build。
- [x] 独立代码复核，修复后复验；更新证据和总清单，仅提交本批并推送 origin/main。

证据目录：Backup/Artifacts/FinalDragonApplied-20261026。已有其它本机差异不纳入本批。

验证：客户端47/47、服务端12/12；独立复审关闭2项Important。证据见本批README。提交/推送以Git记录为准。
