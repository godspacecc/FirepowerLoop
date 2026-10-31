# 开始页商店实施交付

正式开始页新增商店按钮，幸存者直接开局。商店顶部小行显示已有累计金币，中部商品暂未开放，返回/关闭可重开。坏档显示未知余额和错误，不写盘；语言变化刷新。没有购买、重置或兑换。

## 实施与生成

- Prefab稳定布局及ReferenceCollector使用正式UICodeSpawner.AutoUpdateUI生成；延迟回绑未触发后调用同一生成器BindGeneratedUiComponent完成Start和MainMenuShop回绑，非手工View字段接线。
- Config/Localization/survivor.json新增3个key；Config/gen.bat双端成功，generation.log。生成结果仅两语言客户端JSON发生差异。
- 开局中BtnShop禁用且回调检查startingRun和IsShow；同步UIHelper加载不存在异步迟到窗口。
- baseline-current保留修改前工作区；StartUICtrl-HEAD.cs.txt保存HEAD。StartUICtrl直接幸存者开局为本聊天先前修改，baseline-current已含该项，review.patch主要显示本实现者后续增量；提交必须同时纳入直接开局回调。

## 验证

- 已先枚举真实非参数测试。EditMode MainMenuShopTests最多1项，includePassingTests=true；先因商店缺失RED，后正式生成引用契约1/1通过。
- EditMode MainMenuShopInteractionTests最多1项，includePassingTests=true；临时独立钱包打开、关闭、重开、坏档不覆盖、未知余额、恢复及语言变化1/1通过。首次语言断言失败是测试未切换模型，修正测试后通过。未操作玩家金币文件。
- tests.json保存实际MCP结果摘录及入口/上限；red-contract.json保存RED记录。没有完整套件或参数展开。
- source-collection.txt是真实BeginCollect结果：Prefabs/UI/Home/MainMenuShop，正式Prefab路径，startup。Home既有目录收集，无需修改collector。Boot启动下载startup与arcade，UIHelper按需同步加载。
- 正常Play由Boot进入Main后真实Game.YooAssetResourceLoader异步加载成功并显式释放句柄，runtime-load.txt。最初在Boot尚未完成时探测过早得到NullResourceLoader，等待正式初始化后成功。另一次同步探测成功但该旧API成功分支本身不释放句柄，随后以异步显式释放验证并退出Play；没有修改加载器。
- shop-prefab.png和start-prefab.png为正式Prefab隔离渲染。已人工检查商店顶部小余额与开始页全部7入口同时显示的最宽松条件均无溢出；实际发布隐藏部分入口后更少。不是运行加载证据，运行证据另列。

## 边界

未打包或发布，源收集及当前Editor运行加载通过不代表旧发布包已有资源。未提交推送；交root独立审查。changes.json列精确任务文件，review.patch基于工作区基线，其他任务的暂存区未动。

补充：start-prefab.png为全部入口激活的压力布局图，不是正常运行界面；start-normal-prefab.png为正式Prefab模拟正常离线无存档可见状态（闯关/幸存者/商店/设置/退出）的隔离渲染。git diff --check仅报告Unity序列化空字段的行尾空格，未为此改写既有Prefab全文件。
