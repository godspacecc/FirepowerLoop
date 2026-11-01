# 幸存者元素弹头规则与待采用动态预览

2026-11-01，用户提出火焰持续伤害／冰霜减速，并明确“互斥 和穿透弹也互斥吧”。采用本会话首版数值并将火焰、冰霜、穿透组成同一本局三选一路线；首次选择后锁定，所选可继续升3级。爆炸／分裂仍独立互斥，多重可搭配。

火焰：命中后燃烧3秒，每秒造成发射时该发子弹伤害的20%／30%／40%。不足1点的伤害累计；重复命中刷新时间、不增加层数，弱旧弹不覆盖强状态。冰霜：命中后2秒移速降低20%／30%／40%，重复刷新，不修改基础移速，跨到期帧按剩余时段计算距离，近战前摇、冷却及入场动画计时仍使用原战斗时间。只有步枪直接弹命中附加，无人机、爆炸范围伤害、分裂子弹不传播元素。暂停／选卡冻结，清场／重开清理，燃烧死亡复用唯一击杀和掉落事务。

## 功能验证

- SurvivorElementTests：红阶段16/16按缺少功能失败，red.json；首轮12过4失败来自测试试图替换已缓存候选，修正验证顺序后16/16通过；补充后21/21通过，elements-green.json。声明最初最多24，扩展前声明最多32，实际21。
- 补测覆盖全部三级强度、爆炸／分裂不传播、多敌同帧燃烧死亡、冰霜不改变真实近战前摇／冷却。
- 独立复审指出单帧暂停测试存在小数伤害假通过空间；已改为暂停和选卡分别累计40帧／2秒，完整方法Game.Tests.SurvivorElementTests.FireFreezesDuringPauseAndUpgradeChoice 1/1复验通过，pause-green.json。此项已包含在21个不同元素检查中。
- SurvivorConfigTests：17/17通过，config-summary.json及会话真实MCP返回。
- SurvivorUiTests：16/16通过，ui-green.json；新增元素中英文首级和末级文案及标题容纳检查，保留原Prefab／字体／布局。最多20项。
- SurvivorEnemyCombatTests：15/15通过，enemy-green.json。最多24项。
- SurvivorRuntimeTests：74项中73通过、1失败，runtime-initial.json。失败VisibilityHiddenEnemyStillTakesProjectileDamageAndDropsExperience原点(25,20)超出已提交昼夜最大射程24，此问题存在于HEAD；未修改范围逻辑，仅改为镜头Rect(-4,-4,8,8)及展开带之外、但射程内的(12,8)。完整方法1/1通过，runtime-fix.json。没有把最初失败记成单次74/74通过；最多100项。
- 合计143个不同限定检查均有通过证据；全部Unity测试为EditMode、真实短testClass或完整testMethod、includePassingTests=true，未运行全项目套件。
- Config/Defines/beans.xml新增StatusDurationSeconds／StatusStrengthBase／StatusStrengthStep，所有源行保留编号，新增RifleFire／RifleFrost；RiflePierce同组RifleTip。Config/gen.bat双端成功，generation.log；最新已提交Config加本任务权威源在独立目录使用同一个gen.bat再次生成，generation-scoped.log。生成C#与bytes不手改。
- 独立提交索引的全部Game运行C#使用Unity现有编译响应和Roslyn编译，退出0，scoped-compile.log；仅C#编译，无Unity资源构建或Player Build。保留项目已有警告。
- 请求只读审查review_survivor_elements并复审补测和旧夹具修正：未发现确定的运行阻断问题，暂停测试缺口已修复复验。

## 动态预览待采用

[播放5秒动态预览](elements-preview.gif)，100帧／20fps；[静态缩略](preview-still.png)。Preview.cs.txt为临时Unity场景制作脚本，正式主角和两只正式丧尸同尺寸同框，仅克隆资源；采样真实步行动画，手工刷新蒙皮代理避免编辑态缓存僵直，临时火焰粒子／冰霜冷色均未写入正式资产。

0.5秒开始，火焰持续3秒，冰霜持续2秒，首级冰霜期间以80%速度走动，到期复原。文字是视频说明，不是新增游戏HUD。这是独立视觉候选，不是正式场景实录；等待用户明确采用后再制作／接入正式效果及验证实际资源链。

当前批次只完成已确认玩法与可查看预览，正式视觉仍待采用。既有动态资源地址与收集规则未变化，此次规则不新增动态资源；预览AssetDatabase加载不作为发布包或运行资源验证。未整局人工试玩、未验证发布包／设备、未打包。
