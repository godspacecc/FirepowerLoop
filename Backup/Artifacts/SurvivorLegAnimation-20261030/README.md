# 幸存者腿部动画诊断与预览

- 用户反馈幸存者移动没有腿部动画；目标恢复已有跑步动画，不新增动作、改变角色尺寸或武器外观。
- 代码证据：SurvivorSystem.Tick在推进玩家位置后调用SetPlayer，处理战斗后同帧再次调用SetPlayer；ArcadeCombatViewController.SetPlayer每次调用playerMotor.Move；PlayerViewCtrl.Move按本次实际水平位移更新Animator MoveSpeed。第二次同位置移动会将速度置零。
- 正式动作隔离对比：使用GrayboxPlayer和现有基础枪动作，不修改正式资源。左侧每步设置移动后又将移动速度清零，复现重复同步后的状态；右侧每步只保留移动速度3米/秒并播放既有枪械反馈。48帧、12帧/秒，左腿最大角变化2.1936度，右侧41.7959度；最终MoveSpeed分别0和3。
- `leg-comparison.gif`为独立PreviewScene中的现有资源动作实录，不是正式幸存者运行实拍，也不是已完成修复证据。
- 建议修复：每帧只同步一次实际移动，射击朝向与受击保护另作状态刷新，不再次用零位移覆盖移动动画。停止移动时仍自然返回站立，暂停/升级时仍按原规则冻结。
- 用户对右侧已有动作恢复效果及经验曲线初始10/每级+5回复“可以 这两个问题都开始修正吧”，两项方案与开工均已确认。
- 实施：幸存者每帧只调用一次SetPlayer驱动实际移动；末尾改用SetPlayerCombatState仅刷新本帧射击朝向和无敌保护，不清除lastHorizontalMotion或MoveSpeed。原Tick仍传Time.deltaTime，新增TickFrame显式时间入口支持非运行态确定性回归，暂停和单帧时间上限保持。
- 经验配置只改Config/DataCsv/survivor_rule.csv，将InitialExperienceThreshold从5改10、ExperienceThresholdGrowth从3改5，通过Config/gen.bat双端生成；掉率100%、每份1经验及精确同落点合并保留。
- 首次生成遇到同仓并行任务新增SurvivorEnemy表时的表数校验差异，尚未发布产物；该任务更新对应校验后重新执行正式入口成功。本次不提交其表、生成代码或生成入口修改。
- 双端tbsurvivorrule.bytes SHA256一致：363523FF7333C6C8100E00F4707BBE60F40861EFDDFC28A9C3E59FDA9ED5F77C。
- 回归：最初EditMode无真实帧时间使移动用例无法推进，改为显式帧时间后准确复现同帧两次位置同步（2 vs 1）；经验门槛回归准确复现5 vs 10。最终SurvivorConfigTests 10/10、SurvivorRuntimeTests 30/30、GunLocomotionAnimationTests 17/17，合计57项通过，includePassingTests=true。真实Humanoid腿骨运动和移动枪械分层由正式动作测试覆盖。
- 只读审查无阻断，定向diff --check通过。无新动作/模型/资源地址，未资源打包或Player Build，未进行正式幸存者整局人工试玩；实际手感及升级节奏待用户验收。
