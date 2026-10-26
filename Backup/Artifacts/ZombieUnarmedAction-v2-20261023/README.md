# 丧尸空手动作候选 v2

来源：用户明确“丧尸肯定是只能抓和撕咬 怎么可能有武器呢”。本版替代被否决的v1持刀方向，只展示空手丧尸；右侧持枪者是正式主角，保留原尺寸同框比较。

- [可播放对照页](index.html)
- [空手扑抓](shambler-claw-melee-action.gif)
- [近身咬击临时草案](shambler-bite-draft-melee-action.gif)
- [疾行丧尸扑抓](gaunt-claw-melee-action.gif)、[重型丧尸扑抓](bloated-claw-melee-action.gif)

每段80帧、20fps原速、4秒循环，包含持续贴近攻击与准备期间跑开取消。0.4秒前摇保留。本页是正式模型克隆的隔离采样，尚未正式接入，不是单人或联机实战录像。

扑抓使用现有Civilian|Idle_Attack片段，三种丧尸正式Attack触发均能进入攻击状态，临时实例的HeldWeapon挂载数均为0，见inspection.txt。丧尸配置的weapon.zombie-claw是爪击规则身份，不是手持物。

未在现有丧尸动画目录找到独立撕咬片段；当前Generic骨架也没有独立Jaw骨。咬击只用克隆骨骼叠加探身和头部前倾展示动作方向，没有嘴部开合，不冒充已具备完整撕咬动画。采用后仍须完善正式衔接与可读性，再对齐权威伤害与单双人同步。

Unity MCP核对本工程路径、场景和Console后，在NewPreviewScene内采样，结束释放临时场景、相机、网格和材质。帧数、时长及文件哈希见verification.json。未修改正式Prefab、动画、控制器、战斗代码或配置；未运行测试或Player Build。具体动态尚待用户采用。
