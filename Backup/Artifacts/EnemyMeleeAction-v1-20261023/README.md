# 普通近战动作候选 v1

本版持刀方向已被用户2026-10-23“丧尸肯定是只能抓和撕咬 怎么可能有武器呢”否决，仅保留历史记录；当前请查看[丧尸空手候选v2](../ZombieUnarmedAction-v2-20261023/README.md)。本版没有获得正式接入授权。

2026-10-23，来源：用户在0.4秒前摇规则交付后反馈“但是我看没有攻击动作啊”。

这是正式资源克隆的隔离动态候选，尚未正式接入。既有普通怪/丧尸和正式主角保持原尺寸同框，不是当前单人或联机实战录像。

- [可播放对照页](index.html)
- [普通丧尸扑抓](zombie-melee-action.gif)
- [普通持刀怪挥击](basic-melee-action.gif)

每段80帧、20fps原速、4秒循环。第一次持续贴近，展示进入范围后准备0.4秒再挥击；第二次准备中途跑开，取消挥击并追击。采样复用既有片段，临时调整前段/收招节奏；用于确认动作方向，正式衔接、同步和取消清理仍待采用后实现及验证。

## 已核实原因

- 单人伤害规则在完成前摇时才递增AttackSequence，表现层仅消费该序号触发Attack；MeleeWindupActive/Remaining未投影，所以0.4秒等待时没有准备动作。
- 联机SyncEnemies调用SetEnemy时未提供attackSequence，现有敌人快照也没有普通近战准备/攻击序号字段，因此普通攻击无法按权威时机触发动作。
- 正式两种怪的既有Attack触发均能进入Attack状态，见inspection.txt。持刀片段HumanM@Attack1H01_R长1.1秒，丧尸Civilian|Idle_Attack长0.65秒；不是资源不存在。

## 验证与边界

Unity MCP核对本项目路径、Boot及Console后，在编辑态NewPreviewScene内执行；结束关闭隔离场景并释放材质、网格、相机与渲染纹理。使用实际采样骨矩阵临时蒙皮，避免编辑态BakeMesh缓存导致身体和持刀骨骼错位。GIF数量、帧数与总时长自动核验见verification.json。

未修改正式Prefab、AnimationClip、AnimatorController、EnemyView、EnemyCtrl或伤害规则；没有运行测试、Player Build或真人双端验收。本次只做诊断、临时预览和确认记录，待用户采用具体动作后，再接入正式单双人表现并做定向回归。
