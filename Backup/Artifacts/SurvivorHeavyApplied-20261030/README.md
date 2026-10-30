# 幸存者已采用体型与笨重丧尸接入

用户要求后期加入高生命、慢速笨重丧尸，并修正瘦削更矮、笨重更高更胖；对proportions-v2同框动态预览回复“可以”，具体比例和正式接入已确认。

## 最终行为

- 普通丧尸开局出现，固定生命100、速度2.2，原尺寸。
- 瘦削丧尸120活动秒后加入，固定生命55、速度3.06，比例(.9,.82,.9)。
- 笨重丧尸240活动秒后加入，固定生命300、速度1.43，比例(1.5,1.2,1.4)。
- 权重40/25/10，总刷新节奏与当前无存活上限规则保持；基础持剑怪、弓箭手和重型持武器精英仍不加入。空手抓挠沿既有前摇、取消、收招及伤害保护。
- 同期另一聊天用户明确取消生命/伤害/移速时间成长，已由独立提交2c3938ce实现；本批遵从固定属性，不重新启用成长。

## 实现

Config权威源新增独立三轴体型列及bloated候选，经Config/gen.bat双端生成39/35表。共用enemy.csv与角色Prefab/.meta、模型、动画均未改。

正式SurvivorSystem每帧经ArcadeCombatViewController与EnemyCtrl投影实例比例，相对原比例计算，重复刷新不累乘，回池恢复原尺寸，不改变闯关模式体型。水平最大比例用于受击、出生路线与完整身体隐藏检查，抓挠距离随体型匹配。不同半径共用各自距离场，在入场时预建，每帧每种体型只更新一次目标格；过窄的大体型入口尝试下一条路，无法安全出生时不生成。

## 验证

限定Unity EditMode短类、includePassingTests=true，未完整套件：敌池9/9、真实战斗规则11/11、回池体型1/1、资源分发8/8、导航4/4、原运行回归31/31，共64项通过。RED分别复现缺少候选/体型属性/投影接口，见*-red.json；最终见*-green.json。

新增完整TickFrame回归验证正式帧传递体型和大身体出生半径；真实AdvanceBullets验证普通半径之外命中大身体；真实SurvivorNavigation验证小身体通过2米窄缝、大身体被挡；真实EnemyCtrl重复比例投影与回池恢复通过。独立只读审查和补充复核均无阻断。

[采用预览](../SurvivorHeavyZombie-20261030/proportions-v2/heavy-preview.gif)；[正式配置与表现端口回读](heavy-preview.gif)。后者为临时PreviewScene读取正式生成配置，经真实ArcadeCombatViewController.SetEnemyScale→EnemyCtrl.SetPresentationScale渲染，非自然对局；静态和抓挠帧已目视核对，60帧完整解码通过。ProductionReadback.cs.txt和render-readback.py保留制作过程。走路/抓挠采样的可见蒙皮表面最高Y=2.173167，低于当前3米出生隐藏包络，见production-readback.txt。

真实BeginCollect唯一匹配新敌池表及三类丧尸地址，详见collected.txt：表为startup，三类Prefab为arcade；Boot下载startup+arcade，既有EnemyCtrl准备从独立敌池枚举三类ID后预载，沿原句柄释放/取消路径。源.meta与依赖存在，未新增收集项。双端敌池bytes SHA256：e515307e6db5a12d8e5c30f7f99f9aa38534b4acd89d265c8971556ef65040fd。

未资源打包、未Player Build、未刷新发布清单；源收集和隔离AssetDatabase渲染不证明旧发布包包含更新后的配置，真实运行/发布包及包内依赖、整局人工试玩未验。仅提交本任务，不夹带同期成长取消或其它未完成任务；提交推送以Git回执为准。
