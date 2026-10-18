# 敌人局部回弹与死亡布娃娃实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 普通命中局部回弹，死亡切换布娃娃，爆炸击杀按来源向外击飞，并限制手机物理成本、完整支持对象池。

**Architecture:** 沿用 EnemyView / EnemyCtrl 表现与地图生命周期，生命和击杀仍由单人／服务器战斗系统裁决。Prefab 预制简化人形刚体，存活时全部休眠；局部回弹由现有 Update/LateUpdate 分别恢复与叠加。联机仅同步有界死亡表现事实，不同步骨骼物理。

**Tech Stack:** Unity 2022.3、QFramework、Animator、Rigidbody/CharacterJoint、现有协议生成器。

**Spec:** 本聊天用户2026-10-18明确确认：普通受击局部回弹、死亡布娃娃、爆炸击飞、注意手机性能和回池。已授权直接实施。

## Global Constraints

- 不创建worktree、不打包、不提交；保留其它并发工作和全部meta。
- 骨骼物理不参与战斗碰撞、伤害、移动或存档。
- 手机最多4具、桌面最多8具同时保留；每具11个刚体，普通1.4秒／爆炸1.8秒后冻结，2.2秒回池。
- 仅布娃娃地面与布娃娃碰撞；活体、弹丸、其它尸体不受物理干扰。
- 测试按真实类或方法声明范围与上限，includePassingTests=true，不跑完整套件。

## Review Focus

1. 连射时骨骼偏移不累计、不改变根位置，暂停不继续计时。
2. 爆炸大量击杀不超过尸体预算，保留后续生成能力。
3. 回池后Animator、骨骼、速度、碰撞与武器恢复，不保留旧死亡状态。
4. 联机重复快照和换轮不重复播尸体，重连不补播历史死亡。
5. 资源缺失和无骨架敌人保留现有死亡表现，地图卸载无孤儿物理对象。

## Task 1：骨骼表现、Prefab制作与回池

- [x] 新增 EnemyPhysicalReactionTests，先确认真实Prefab缺少简化骨架及组件而失败。
- [x] 新增 Controller/View/Map/EnemyPhysicalReaction.cs；提供 PlayHit(Vector3)、Tick(float)、LateTick()、BeginDeath(Vector3,bool)、ResetForReuse()。
- [x] 新增 Tools/Unity/EnemyPhysicalReactionAuthoring.cs，为四种正式敌人预制11刚体与10关节，制作独立物理层。
- [x] EnemyView接入命中、死亡及禁用复位；EnemyCtrl现有生命周期推进并限制尸体预算，地图持有一个专用物理地面。
- [x] 验证骨架、局部回弹、不同击飞力度、冻结、禁用与反复复用，以及预算与清场。

## Task 2：单人与联机死亡事实

- [x] 单人死亡入口传递方向和爆炸标记；爆炸／炸弹真实结算位置传入来源。
- [x] 服务端保留最多24条死亡事实，包含实体ID、方向、爆炸标记、轮代次；正式协议追加后经Tools/gen-protocol.ps1生成。
- [x] 客户端只为已经可见且本次消失的敌人消费同轮死亡事实；重复快照、重连和换图不复播。
- [x] 定向验证普通／爆炸、重复移除与联机快照隔离，检查服务器克隆路径。

## Task 3：验证与交付

- [x] 定向回归现有敌人视觉、重型、炸弹及开场测试，服务端独立检查与C#编译。
- [x] 编辑器物理场景中实际观察倒地、击飞、冻结和复用；保留证据并明确实体手机／双客户端未验范围。
- [x] 请求一次只读代码审查并处理问题，更新当前功能资料。

## 执行记录

- 2026-10-18：已确认四种街机Prefab均为Humanoid，现有0刚体。已有AppStateSystem通过Time.timeScale暂停，EnemyCtrl持有死亡延迟回池列表。
- 2026-10-18完成：四种正式人形预制11刚体／10关节，手机4具／桌面8具预算；冻结与回池、清场复位、单人与服务端死亡来源及有界联机事实均已接入。协议通过正式脚本生成。
- 新测试先观察缺少骨架、缺少爆炸元数据失败，再实现；补充电球误判、载桶连锁／地面桶来源和过早空中冻结回归，确认失败后修复。旧Prefab禁止全部子碰撞体的断言改为允许专用休眠骨架，Prefab资源检查使用自身Rigidbody。
- 定向EditMode最终累计67项通过：骨架11、死亡来源8、敌人视觉14（13原通过＋契约单项修复后通过）、重型4、炸弹6、开场24；每次只运行明确过滤的类／方法并返回通过项。服务端独立6/6，Hotfix编译0警告0错误。
- 隔离Editor物理场景8具／88刚体120步：爆炸最大抬升0.719、最终髋骨最大高度0.166，数值有限、冻结稳定、回池休眠；预览结束Console无新增Error、Boot保持磁盘清洁。手机真机帧率和真实双客户端仍待验。
- 只读审查发现并修复通用圆形伤害误算爆炸、地面桶／载桶／环境爆炸来源遗漏、预览RenderTexture释放顺序；复核无剩余阻塞项。证据：[记录](../../Plans/Artifacts/EnemyRagdoll-20261018/README.md)。
