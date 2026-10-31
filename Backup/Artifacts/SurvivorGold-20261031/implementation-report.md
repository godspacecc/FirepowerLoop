# 幸存者金币实现阶段报告

状态：部分实现，需要接续。未提交或推送。

新增文件：
- Client/Assets/Game/Scripts/Utility/Save/SurvivorGoldFileStore.cs 及Unity生成meta
- Client/Assets/Tests/EditMode/SurvivorGoldStoreTests.cs 及Unity生成meta

未修改任何既有共享文件，所以尚无共享文件开始基线。后续编辑前必须复制开始内容，工作区存在大量其他任务修改。

验证：Unity MCP已核实 E:/GitHub/FirepowerLoop/Client，Boot场景。实际枚举测试确认 SurvivorGoldStoreTests.PickupPersistsExactlyOnceAcrossReopen 存在；限定EditMode短类名，最大1项，includePassingTests=true。先红灯0/1（缺少独立金币存档），新增实现后绿灯1/1（3.534秒）。测试前经授权从磁盘重载Boot，丢弃临时脏状态。无打包。

已实现：独立文件、每份1、稳定领取编号幂等、文件flush及原子替换、重新实例化余额不丢、基本坏档拒绝。

待完善：当前每份保留一条receipt，会长期增长，需改为有界活动局回执或类似机制并保留跨重启安全。失败、坏档、溢出等测试尚未增加；余额等于回执数量的约束在压缩回执设计后必须修改。

未完成：掉落、拾取接入、模型/HUD/结算替换、入口UI及生成器、配置与双端生成、动态资源真实收集和运行链路验证、UI实拍、独立审查、提交推送。当前功能不能交付。

## 接续阶段：有界事务（尚未验证绿灯）

- 新存档改为固定大小字段 Version/Balance/ActiveRun/Sequence，不再保存逐金币receipt。
- TryBeginRun(string run,out long balance,out string error)：切换活动局令牌，新局重置领取水位但保留余额；同令牌幂等。
- TryCollect仍收run:seq，旧局拒绝、跳号拒绝、已提交seq幂等；新序号原子+1。
- 红灯证据storage-bounded-red.json：1项失败，缺少TryBeginRun。
- 实施后绿灯调用180秒超时，storage-bounded-green.json内容是超时，不能当通过。后续testResults只读查询亦长时间无响应。
- 修改SurvivorRuntimeTests两条击杀分数断言由ScorePerKill改为0（取消计分）。尚未取得红灯且尚未修改运行时，当前这两项预期失败。
- 已备份SurvivorSystem.cs、SurvivorModel.cs、SurvivorRuntimeTests.cs到baseline对应相对路径。

### 下一步运行时接线

已有IArcadeCombatViewPort.SetDrop(id,ArcadeDropDefinition,Vector2)，可复用catalog.Drops中arcade.drop.gold.small的地址创建amount=1的幸存者表现定义；LootCtrl现有准备已预加载所有catalog.Drops视觉。
新增SurvivorSystem.Gold.cs负责goldStore、goldRunToken、goldSequence、Dictionary<int,Vector2> goldDrops和随机流；TryPrepare校验并读余额，TryStart在Stop之后TryBeginRun(Guid.NewGuid().ToString("N"))。Tick金币拾取必须TryCollect成功后才移除、推进seq和本局数量。失败保留地面金币并提示，节流重试避免每帧磁盘写。Stop清除运行态token/drop，累计余额不变。
击杀移除Score增长，按ArcadeEnemyDropRules.Chance(catalog.Rule...)随机掉1份金币；经验独立。新增金币须考虑MaxDrops空间预算（不得靠溢出每次掉落累积到一个金币后仍只给1）。
Model新增本局金币与累计余额独立字段供HUD/结果UI，旧Score结果文件兼容读取但不兑换。UI字段通过正式生成器处理，不手改生成View。

仍需限定真实测试覆盖拾取成功/失败/重复、旧局失效与新局余额，配置去掉ScorePerKill源字段并gen.bat生成（此处尚未修改）。资源检查与UI全部未完成。

## 最终阻塞状态（本阶段停止）

- tests_run与testResults分别180秒超时，均已结束；root确认computer-use恢复也因native pipe unavailable失败。停止Unity请求，无正在运行的本agent工具cell。
- 按root要求恢复仅本任务改动的两条SurvivorRuntimeTests分数断言；内容与保存的baseline相同。未修改任何现有生产运行时。
- 保留新增SurvivorGoldFileStore.cs、SurvivorGoldStoreTests.cs及meta作为未接入草稿。有界版本只取得红灯，绿灯没有结果，严禁标成通过。
- review.patch已重建，仅含两个新增C#文件。尚未提交/推送。
- 整体任务仍未完成，恢复Unity后需继续存档失败测试、runtime/配置/UI正式生成和资源验证。前一阶段1/1通过只证明旧无界版本，不能证明当前有界版本。

## 完整实现已进入审查（2026-10-31重试后）

- 当前实现：独立金币原子文件（有界活动局/领取水位），击杀概率落金币每份1，拾取先落盘再移除；经验独立，金币与经验各受MaxDrops限制。失败保留金币并2秒限频重试，失败提示沿Notify。取消击杀积分，旧Score字段仅保留存档兼容；ScorePerKill源/运行配置/双端生成已移除。
- 新入口先显示余额，返回/关闭均清除回调，开始按钮提交既有开局流程；坏档显示错误禁用开始，返回重新打开可重试。HUD显示本局金币，结果显示本局/累计。局外消费未加入。
- 本次实际验证：storage-final.json 3/3；runtime-green.json 38/38（曾声明37方法，参数化展开实际38，记录计数差异）；ui-tests.json 9/10，新增入口回绑缺失通过正式生成器BindGeneratedUiComponent修复，entry-final.json针对该方法1/1通过。不是重跑全套。
- 真实源收集：source-collection-final.json：Prefabs/UI/Survivor/SurvivorEntry，startup;arcade；Prefabs/SceneObjects/ArcadeDropCoin，arcade。既有LootCtrl准备遍历catalog.Drops预加载金币；入口目录已有startup/arcade收集。未更改collector设置。
- generation-final.log：唯一Config/gen.bat双端生成成功。未手改生成C#或bytes。
- entry-prefab-final.png为正式Prefab编辑器渲染，非游戏运行实拍。布局已校正左对齐与实色按钮、关闭按钮；原示意金币图标为占位，正式当前以金色数字显示。
- 运行/发布包实际加载尚未验证，需root审查期间继续真实YooAsset loader读资源并release；未打包。
- review.patch按baseline-current只取本任务增量。changes.json列出所有修改/新增文件。两份客户端本地化输出首次生成前未单独保存基线，提交时只选Config/Localization/survivor.json所对应keys的生成差异，勿夹带其他任务。
- 未提交/推送；总清单由root在审查通过后更新金币分区。

### 真实运行加载补充（审查前）

正常进入Play启动，DefaultPackage.InitializeStatus=Succeed；使用当前真实YooAsset package.LoadAssetSync<GameObject>读取Prefabs/UI/Survivor/SurvivorEntry和Prefabs/SceneObjects/ArcadeDropCoin，两者均成功，句柄finally.Release，详见runtime-resources.json。随后退出Play。未开始对局、未调用钱包、未手动构建/打包。本证据仅代表当前编辑器运行环境，发布包与设备未验证。

最终入口实拍为entry-prefab-final.png，工具RenderEntry.cs.txt。正式界面现含返回与关闭按钮、金色开始按钮，已无开始页装饰。

## 独立审查补测已通过

- GoldWriteFailureKeepsDropUntilCooldownRetrySucceeds：限定完整方法、上限1、includePassingTests=true，1/1通过，原始write-failure.json。先成功BeginGoldRun创建真实账本，随后Windows只读属性强制TryCollect实际原子替换失败；断言金币集合保留、视图RemoveDrop=0、本局/累计余额仍0、账本内容不变。恢复可写后1秒仍不移除，2.1秒重试成功，再Tick仍只一次移除及+1。为避免测试加载无关浮动提示，预置同一个已提示错误；不替代文件保存或拾取逻辑。
- CloseDoubleClickAndCorruptWalletRespectEntryLifecycle：限定完整方法、上限1、includePassingTests=true，1/1通过，原始entry-interactions.json。真实SurvivorEntryUICtrl、正式Prefab，临时架构与临时账本；Show/Configure/实际关闭和开始按钮事件证明关闭清回调、重复提交一次，坏档禁用开始并显示错误，返回后恢复文件重开刷新余额及按钮，语言事件后余额稳定。
- 本轮仅新增验证，没有生产代码修改；fix.patch为本轮测试增量，review.patch和changes.json已更新完整任务包。运行YooAsset资源成功证据仍为runtime-resources.json，发布包/设备未验证。
