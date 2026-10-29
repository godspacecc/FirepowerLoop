# 实施检查点（未完成）

已修改 TestCmdSystem.cs、ArcadeCombatSystem.cs、TestCmdTests.cs。
单人 drops/god 最小入口已编译；无敌绑定当前现场引用，并在 EndCombat 清除。不修改生命或持久强化。

## 验证
RED: TestCmdTests 18 项，14 通过、4 失败。其中新增三参数用例均因 UsageError 而失败；既有 CameraCommandSwitchesModeCaseInsensitivelyAndIsIdempotent 失败，未修改 camera。
阶段验证：具名完整方法 Game.Tests.TestCmdTests.NewArcadeCommandsRecognizeSyntaxWithoutActiveBattle，最大三例，includePassingTests=true：god/drops 通过，spawn 仍失败（尚未实现）。
git diff --check 返回0，仅已有文件换行警告。
一次不完整 testMethod 过滤返回0；未运行空过滤或全套。后续使用实际完整方法。

## 未完成
spawn、goto、双人权威实现、Config生成、可读名称解析、有效现场伤害/生命周期测试与边界测试均未完成。现有无敌需要实际伤害测试，不能据目前测试声明完成。
当前未提交、未推送，原三项脏资源未修改。

## 结构发现
ArcadeRunSystem 用具体 ArcadeRunFileStore，所有持久化集中其 Inspect/Save/SaveAsync/Delete。建议 goto 成功切换独立内存 store，保留正常 store；离开测试局恢复正常 store，避免异步持久化串局。

## 第二检查点
单人spawn已接正式SpawnQueuedEnemy(runtime,testEnemyId)，指定ID不消费现有SpawnQueue；数量1..20、当前group与全局MaxAlive容量验证；挑战/终Boss/墓穴场景拒绝。TestMutationReady拒绝过渡、结算、出口、宝藏、复活、地图Loading。drops使用ProductNameKey与LocalizationUtility查真实名称，无名称输出类型。god在MapLoadingStarted及EndCombat均清除。

实际限定测试（均includePassingTests=true）：
- Game.Tests.TestCmdTests.NewArcadeCommandsRecognizeSyntaxWithoutActiveBattle：最大3例，3/3通过。
- Game.Tests.TestCmdTests.ArcadeStatusGoldAndResourcesMutateOnlyTheActiveCombatRuntime：最大1例，1/1通过；增加实际DamagePlayer无敌开关/离场断言（离场RED后补实现GREEN），以及非法刷怪不动队列/敌人断言。
- Game.Tests.TestCmdTests.RequestedEnemySpawnPreservesWaveQueueAndEnforcesCapacity：最大1例，1/1通过；真实Luban配置、实际生成指定敌人、原队列保持、未知ID拒绝、填满容量后拒绝。

仍未完成：goto隔离、服务端所有新命令、Config权威目录生成、帮助更新及总任务审查；不能提交。
具体后续路径：ArcadeRunFileStore集中持久化，Async实现位于其他partial文件需rg SaveAsync。ArcadeCombatSystem.RoundLayouts.cs StartContentRound处理地图轮事务、BeginCombat支持RoundCheckpoint恢复。所有场地复用MapIds.Arcade。服务端ExecuteTestCommand位于OnlineArcadeRunSystem.cs 157行附近，SpawnQueuedEnemy及DamagePlayerCore在同文件，Actor/BattleRuntime定义需查Model目录。

## 第三检查点：单人goto
独立CreateTransient store统一Inspect/Save/SaveAsync/Delete，正常store引用保留；goto候选清空场内检查点/待奖励，按路线版本遍历NextArenas，当前序号用保存场地，旧版多候选明确拒绝。通过真实BeginCombat+StartIslandRound/StartContentRound处理指定轮；等待地图准备、命令暂停时只推进资源事务，不推进玩法。失败退回原安全节点；重入拒绝；架构释放使续体失效。返回只在合法阶段且正常Inspect成功后恢复store；测试结果确认删除仅删除memory并恢复normal。

具名限定测试（includePassingTests=true，最大各1项）：TransientArcadeStoreNeverWritesOrDeletesNormalSaveFiles RED工厂缺失→GREEN；GotoUsesRouteOrdinalAndKeepsNormalDiskNodeOnReturn GREEN，覆盖非法序号/轮数磁盘不变、2关3轮config21/WaveIndex2、加载失败退回、返回正常第1关、旧v1第9关分支歧义拒绝、测试局最后一命死亡结果确认后正常存档字节仍原样且可恢复。原始goto输出见goto-test-result.json。

既有camera失败精确信息：Game.Tests.TestCmdTests.CameraCommandSwitchesModeCaseInsensitivelyAndIsIdempotent，Expected: True / But was: False；初次RED阶段未开includeStacktrace，无法据当时输出确定哪个Assert行；该测试与本次改动无关，未修camera。

剩余：双人全部新命令、Config目录及生成、帮助、最终独立审查；单人goto尚建议独立复核异常/场景释放边界。未提交。

## 主执行者审查修复
- 相机基线失败：旧夹具发送PlayerReadyEvent且未设置活动地图，当前相机仅订阅ArcadePlayerReadyEvent。只更新夹具的地图与事件，同一完整方法最大1项由失败转为1/1通过，未改相机运行时代码。
- 独立审查P1：二次goto同步入场异常会经RejectArenaEntry提前退出，旧goto续体再恢复先前测试store。修复：成功退出使等待中的goto代际失效，常规回滚保存并恢复两个store引用。新增同步Begin/SetPlayer抛错回归；移除代际修复后明确Expected 1/Actual 2失败，恢复后通过。原始输出goto-review-fix-red.json和goto-review-fix-green.json。该完整方法同时覆盖先前目标/加载失败/退出/死亡保存检查。

## 第四检查点：服务器与Config
服务端ExecuteTestCommand新增drops/god/spawn/goto，沿既有房主、双方在线验证，额外拒绝轮资源未就绪、结算/出口、死亡或复活现场。成功后复用BattleRevision与BroadcastRunChanged。god只房主runtime且快照Invulnerable广播；新Battle自动清除。spawn采用正式SpawnQueuedEnemy指定ID参数，不消耗队列/游标，恢复SpawnedInWave/NextSpawnAt预算，数量/ID/容量先验证，墓穴等不支持现场拒绝。goto先PrepareGotoForTests返回候选Record/Battle，再一次替换；路线映射与旧分支歧义语义同单人；客户端按既有RoundGeneration握手加载。
NormalRecordBeforeTestGoto仅Actor内存字段，CommitWithCandidate对测试局不写盘，其他局保存快照读取测试actor的正常记录；测试局死亡/确认结束在TryCommitRunFailure恢复正常actor并广播，不删除正常节点。

服务端限定命令：dotnet run --project Tools/ArcadeFullRouteOnlineChecks -- testClass=ArcadeCommandOnline maxTests=1 includePassingTests=true。
RED输出：FAIL ArcadeCommandOnline.AuthorityCommands: missing SetInvincibleForTests。
GREEN详见server-command-check.log；真实god伤害与仅房主范围、指定EnemyId生成、队列/游标/出生预算保持、非法数量、容量上限、goto实际2关3轮config21/WaveIndex2与非法目标/旧分支拒绝、Prepare不修改actor、测试局CommitWithCandidate不写正常文件。尚未做真实联网两客户端交互回归；房主/断线守卫为既有权威代码路径且新增加载守卫已编译。

Config/DataCsv/test_cmd_entry.csv新增5项；spawn例使用实际enemy.arcade.basic。Config/gen.bat执行成功，日志config-generation.log，双端生成目录完成；test_cmd_entry表是客户端组，实际差异仅tbtestcmdentry.bytes。服务端帮助同步所有新语法。git diff --check Server/Config/Tools通过。

还需主执行者独立审查、客户端最终限定测试/生成目录检查、整体风险收尾与提交推送。服务端无真实服务重启或联网验证声明。

## 独立审查修复批次（3项）
1. 重复同场goto：PrepareGotoForTests把新Battle.RoundGeneration设为大于当前活动现场及Prepare默认代次的值，并同步候选记录；BattleRevision可重置，客户端因新代次重新准备地图。
2. 结束测试局：RestoreNormalAfterTestGoto克隆正常内容，内存Revision设为max(正常记录,测试记录)+1，再恢复Actor并广播；不写正常文件。TryCommitRunFailure统一调用该事务。
3. 商店区域同Battle切图：TickSlayerMartRegion提升RoundGeneration时显式清除每个player.TestInvincible。其它正式轮切换与挑战使用新Battle/player，测试无敌自然为false；协议复制字段不是权威场景推进。

服务器限定RED证据：review-generation-red.log（repeated same-arena goto reused generation）；review-revision-red.log（实际恢复保留旧版本的行为回退验证：restored normal revision rejected by client）；review-region-red.log（same-runtime region switch retained god）。修复后同一具名命令maxTests=1、includePassingTests=true绿色，review-server-green.log。新增断言包括重复同场goto代次/候选一致、旧代次SubmitInput ready拒绝、新代次ready接受、正常恢复Revision>测试版本且正常文件字节保留、真实TickSlayerMartRegion同runtime清无敌。

客户端限定OnlineArcadeCommandGateTests，最大2项，includePassingTests=true，2/2通过，原始工具输出review-client-gates-green.json。调用真实OnRunChanged→ApplyAuthoritativeSnapshot：新代次低BattleRevision通过门禁，触发BeginRoundTransition且未加载时IsRoundPresented=false；调用真实IsStaleRunSnapshot：旧正常Revision1被拒，新Revision501接受。未启动完整联网传输。

git diff --check本批文件通过；修复没有改根执行者已修单人goto与camera夹具。
