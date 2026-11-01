# 幸存者强化卡第五版交付验证

采用记录：2026-11-01用户对v5预览回复“可以”；执行总清单为完整进度入口。

## 行为

点击整卡选择、悬停高亮，无独立选择按钮；每卡下面循环箭头与剩余数。整局共用3次跨升级不补，每次只刷新所点卡槽，另两张不变。排除当前三张及未满足武器/路线/满级条件的项，没有合法替代不扣次数；刷新不发强化、不消费经验、不解除选卡或菜单暂停。首次候选仍保底无人机，主动刷新该卡视为放弃该候选。新局恢复配置预算3。

## 正式资源

两份原地址Prefab已保存；宿主CustomListView直接引用外部Box，UI与Box正式生成器运行并回绑。所有布局、图标几何与引用序列化在Prefab；新Graphic和整卡Button子类仅负责绘图/交互反馈，无Update或规则写入。卡片命名已整理，说明基准20（旧24），数值26、辅助17/16。中英文首级及末级实际绑定均检查不超出卡片。

formal-ui.png、formal-hover.png、formal-refreshed.png为正式Prefab、生成View与实际Controller隔离实拍，不是实际战斗截图；同源临时字库仅用于截图，不修改正式字体图集。formal-interaction.txt为实际指针回调证据。

## 验证

62个不同的限定EditMode案例全部通过：规则10、实际刷新交互5、配置17、既有UI16、既有纯规则14（报告*-green.json，includePassingTests=true）。新规则先RED 0/10，再GREEN10/10；UI接入先3/4失败于未绑定，再最终5/5通过。排版回归定位升级值重复单位超高后，压缩为一次单位显示；旧双参数构造API保留，旧标题回归按采用的新短名称更新。

Config/Defines与DataCsv为唯一权威，Config/gen.bat双端生成完成（generate-final.log），字符串Id主键不变；本地化修改归各键既有权威源，避免重复来源。

## 资源边界

source-collection.txt来自真实BeginCollect(false,false)，精确地址Prefabs/UI/Survivor/SurvivorUpgrade唯一，标签startup/arcade，依赖包含外部Box、弹药图及同源字体。BaseUICtrl由ResourceAddress.UiPrefab解析相同地址，经UIHelper按需缓存加载；Boot入口下载startup+arcade后才可请求UI，沿用既有句柄所有权。

runtime-load.txt：正常Boot Play中的Game.YooAssetResourceLoader、EditorSimulateMode真实异步加载通过，宿主/Box/生成引用/图标/字体/反馈引用有效，句柄released=True。未启动战斗、不写玩家结果，检查后退出Play。此证据不代表旧发布包内容，未打包或发布。

独立review_upgrade_cards未发现P1/P2，见review.md。提交仅包含本任务，使用独立index避开共享工作区陈旧暂存内容。
