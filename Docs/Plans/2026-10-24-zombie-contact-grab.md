# 丧尸贴身抓挠正式接入计划

> 执行者按 executing-plans 在当前工作目录逐项完成；不默认创建worktree。实现后进行一次独立只读审查。

**目标：** 将用户已采用的v4接入单人与联机：三类空手丧尸中心距离0.9米才停步起手，完整0.4秒后抓到身体，离开取消，收招与原冷却共存。

**架构：** Config既有AttackRange表达攻击距离，不改变碰撞半径。双端System持有前摇和收招剩余量，网络只读投影；EnemyCtrl/EnemyView通过既有Update/LateUpdate叠加已采用骨骼动作，不新增逐敌人Update、不从动画计算伤害。

**技术栈：** Unity/C#、QFramework、Luban、ET生成协议及限定EditMode/.NET检查。

**规格：** [执行总清单](执行总清单.md)“2026-10-24贴身v4正式采用”；[v4动态和接触证据](Artifacts/ZombieContactGrab-v4-20261023/README.md)。撕咬草案不在采用范围。

## 约束

- 不触发Player Build；Unity操作前确认本项目/场景/Console。
- 配置仅改Config并由gen.bat双端生成，协议仅改Config/Proto并由Tools/gen-protocol.ps1生成。
- 只提交本任务差异，保留并行会话工作；验证及审查通过后中文提交并推送origin/main。
- 所有测试使用实际短testClass或完整testMethod，includePassingTests=true并事先声明最大数量。

## 任务1：距离与权威状态

- [x] 在ArcadeEnemyDropTests和MeleeWindupOnlineChecks增加0.9米外不起手、追到0.9米停步、等待期间不挤入玩家、跑开被追上仍重新前摇的回归，先观察RED。
- [x] 三个enemy.arcade.zombie.*的AttackRange改0.9；保留生命/掉落/碰撞半径/远程/Boss专用行为。复用Config/gen.bat生成双端表并更新指纹。
- [x] 双端普通与特殊接触入口使用各自攻击距离；离开先取消，再追至攻击距离，普通丧尸前摇与收招期间停步。增加仅瞬时的MeleeRecoveryRemaining，命中后0.55秒收招，池化/中断清理。
- [x] 定向验证客户端原52项+新增回归（上限60），服务端MeleeWindupOnline原7项+新检查（审查补充群怪/换位后上限14）。

## 任务2：已采用动作和联机投影

- [x] 新增正式骨骼姿态辅助及EnemyView.Melee：复用v4后拉/前抓/内撕目标，仅改变骨骼旋转，原尺寸/骨长保持；动画前还原，LateUpdate叠加，取消/死亡/受控/回池恢复，不累积。
- [x] 前摇/收招只采样System给出的进度；命中进度0.4与伤害同步，不通过旧Attack触发在命中后重新起手。正常丧尸、保留正式模型的背桶丧尸及墓穴专用正式演员复用同入口；专用演员仍保留现身/换位特效。
- [x] 增加默认可忽略的SetEnemyMelee端口，经EnemyCtrl转发；面向当前权威目标，联机不固定面向本机。
- [x] 协议追加前摇时长/剩余量和收招剩余量，生成后补齐服务器消息/克隆和客户端快照/投影。首快照、重复快照、重连和晚加载直接采样当前阶段。
- [x] 新增限定ArcadeZombieGrabVisualTests（上限8）和服务端快照、群怪分离和真实暗影击倒换位检查；核对动作幅度、重复采样、回池及同步。

## 任务3：正式验证与交付

- [x] 用真实权威推进及正式表现入口录制正式模型/主角同框的接近、0.4秒命中、离开取消，核对接触和生命变化；不将候选录像冒充实战。
- [x] 独立只读审查完整本项差异，修复重要问题并复跑受影响范围；记录真实限制。
- [x] 更新总清单和证据，检查独立暂存投影只含本项。
- Git收尾：按长期授权执行中文提交、推送origin/main并核实远端；实际提交号及推送结果以Git记录和交付回执为准。

## 实际验证记录

- 规则RED：原客户端停步/追上两项及5个姿态检查先失败；服务端先7/10通过，新距离3项失败。
- 第一轮客户端57项曾因并行预览产生无标题场景异常日志而56/57，重新执行57/57通过；不将异常当作规则失败或跳过。
- 审查新增RED：客户端单项群怪挤压在0.5925米失败，server群怪/真实换位2项失败（12/14）；修复后14/14通过。
- 最终限定客户端：ArcadeEnemyDropTests 58/58、ArcadeZombieGrabVisualTests 8/8、NextStageViewTests 12/12，共78项；includePassingTests=true。服务端MeleeWindupOnline 14/14，最大14。
- 群怪分离保留攻击站位；Herd和NextStages把取消视为中断并清理收招。专用演员新增权威采样和旧Attack抑制。独立只读审查和修复复核通过。

- 正式动态三种各100帧/20fps/5秒；0.9米停步、完整0.40秒命中100→90，手指网格距离1.35/1.18/1.49厘米，各1次命中及1次跑开取消。CPU采样代理网格仅用于隔离录制，回池恢复真实Renderer后再次隐藏代理对应源，避免画面双层。
- 最终Editor：本项目Boot场景未脏，compiling=False、scriptCompilationFailed=False；无Player Build。
