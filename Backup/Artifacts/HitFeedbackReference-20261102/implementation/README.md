# 幸存者采用版受击交付

用户采用第三版顺攻击推力反馈后正式接入：玩家躯干12度/0.24秒、敌人30度/0.30秒，头部额外同向5/9度及末段小回弹；接触亮点0.067秒、敌人5滴暗红液滴0.22秒、玩家短红边0.2秒。预览箭头未接入。

来源由实际伤害结算传入：近战/自爆位置、敌方法术与己方弹丸速度、爆炸/导弹落点；零伤害和首次生命同步不触发，持续伤害无明确来源不伪造偏倒。伤害、移动、无敌、死亡物理规则保持原有行为。仅幸存者显式启用，闯关与联机仍保留原反馈。

## 验证

- 新增20个正式演员/真实伤害入口/HUD/控制器时钟回归通过；初始14例先失败于缺少新版入口。空血条生命同步取消反馈的边界也先见红、修复后通过。
- 相邻旧玩家受击/倒地/救援9、普通敌人回弹1、元素表现16、模式准备上下文3，通过；共49个不同限定案例，includePassingTests=true，无全套。额外尝试参数化死亡方法全名过滤时Runner返回未找到，未执行、未计入通过数，也未扩大过滤回退。
- 实际BeginCollect验证11个唯一请求地址：GrayboxPlayer的startup、9种丧尸的arcade、HUD的startup/arcade；完整依赖查询包含正式嵌套反馈Prefab和两种材质。快速收集模式不填DependAssets，依赖通过正式完整查询复核，并非用AssetDatabase存在替代。
- 正常Boot进入Play，Game.YooAssetResourceLoader / DefaultPackage / EditorSimulateMode，11个真实异步加载全部通过、11个句柄全部释放。玩家/丧尸实际激活再禁用均清掉反馈。首轮验证脚本误选玩家武器Animator，改用生产PlayerView.Animator复跑通过，保留首轮记录。
- 资源仍由既有玩家/敌人准备事务和HUD生命周期加载，新反馈是序列化依赖，无新增独立动态地址；幸存者敌人模板在入场准备中验证。既有startup与arcade下载流程沿用。未执行资源打包、模拟构建、Player Build或发布，不能据此宣称旧安装包已更新。
- [正式动态实拍](formal/HitFeedbackFormal.gif)：正式组件与Prefab隔离手动采样，216帧30fps/7.2秒，已查看左右命中关键帧；不是整局游玩录像，不证明长期战斗/真机体验。
- 独立审查的暂停时钟、准备范围门禁两项已关闭；空血条同步与显式Reset也已复核；注释已补齐。

## 工作区边界

另一任务并行增加无人机喷火改装，曾引入Drone.cs插值global::的编译错误，已以括号最小修正以完成验证，但该独立任务代码与配置不进入本次提交。共享文件通过私有index仅提交受击相关片段；默认陈旧index不修改。新20例在修正外部编译错误后已重新通过。

## 证据

[新20例](hitFinalGreen.json)、[初始红灯](red.json)、[生命同步边界红灯](healthRed.json)、[旧玩家](legacyPlayer.json)、[旧敌人](legacyEnemy.json)、[元素](legacyElements.json)、[准备](preparation.json)、[源收集](source-collection.txt)、[真实加载及释放](runtime-load.txt)、[独立审查](review.md)。

最终编辑器回读：本项目Boot干净、非Play，Console 0错误、10警告；警告未当作清零或全项目无问题的证据。
