# 幸存者采用版农场正式接入

2026-10-31用户查看[三维地形候选](../SurvivorFarmLoadingPreview-20261031/README.md)后回复“可以”，确认布局、外观和正式接入。当前幸存者使用独立场地，普通闯关农场保持原环境和四轮布局；旧三面山墙围河候选不采用。

[正式资源参考角度](farm-reference-angle.png) · [俯视全景](farm-overview.png) · [主角同框尺寸](farm-player-scale.png)

采用版草地弯土路、低破石墙、木栅、棚屋、圆草捆、外围树木和远景山脊已经制作为正式资源。16个材质合并绘制组，92939顶点，没有逐物件Update、Collider、Animator或业务MonoBehaviour。正式Prefab含27个空Transform足迹标记，供图形/配置核对；实际碰撞体仍由地图控制器从权威配置创建。棚屋、草捆、树干和石墙/木栅有对应阻挡，墙体缺口连通。小草和地面碎石仅作地表细节。

## 配置与运行链

- `Config/DataCsv/survivor_map_layout.csv` 独立布局表复用既有ArcadeRoundLayoutConfig数据类型，不加入普通闯关布局目录；规则表LayoutId引用该表。原 `arcade_round_layout.csv` 及普通农场50处阻挡保持。
- `survivor_rule.EnvironmentPrefabName` 解析为 `Prefabs/SceneObjects/SurvivorFarmVisual`。SurvivorSystem使用独立只读Layout及其障碍，Begin后指定环境，再进入既有SetRoundLayout加载事务。现有昼夜循环、尸潮、爬出和金币/经验逻辑保持各自所有权。
- 环境加载就绪前不交接战斗；失败保留错误并允许同布局重试。过期回调不能恢复旧地图；End清除覆盖地址并释放句柄。幸存者不预载普通农场牛，普通闯关仍正常等待农场和牛模板。
- 只改Config权威源并通过 `Config/gen.bat` 双端生成，最新基线下客户端41表、服务端37表。独立布局bytes双端SHA256一致：F69A99C6D02C6CA329D3F2481EE6A20D3D53B8AF2EDC87BAAF9DF22A80B32574。规则及布局生成记录见generation.log；生成代码/bytes未手改。

## 限定验证

全部EditMode、includePassingTests=true，无全套/无无效过滤回退：

|范围|结果/最终上限|记录|
|---|---|---|
|SurvivorFarmLayoutTests|7/7|layout-green.json|
|ArcadeEarlyStageLoadingTests|10/10|loading-green.json|
|SurvivorConfigTests|15/15|configuration-green.json|
|SurvivorGroundEntryTests|14/14|ground-entry-green.json|
|SurvivorRuntimeTests.MovementTickDoesNotClearAnimationWithSecondMove|1/1|movement-green.json|

共47项不同检查。初始布局5项有4项真实RED（缺少独立布局/正式Prefab），普通农场原布局1项通过；真实收集单方法先复现缺少新环境地址，补入精确collector后通过。共享工作区的爬出类在执行时已有其他会话新增3项，实际14项（早先声明上限11未计入，保留实际结果，不将其记为11项运行）；没有扩大到完整套件。

正式Prefab在采用版相同镜头、灯光下隔离渲染，三机位已查看；与采用图平均RGB差异每通道小于0.013/255，主要为圆草捆线条BakeMesh微差，见image-comparison.txt。不是自然对局或性能实录。重制首次引入Unity URP材质版本子资源，此后67份资源/meta/Prefab/布局CSV重复制作逐字稳定，见rebake-check-second.json；没有删除或重建既有meta。制作工具只保存本任务目标资产，未调用全局SaveAssets。

独立只读审查及增量复核无待修复问题，见review.md。Unity Boot结束检查为编辑态、未脏；没有保存场景变化。

提交隔离补证：本次将最新已提交的Config权威源加上本地图改动复制到非Unity临时快照，通过同一Config/gen.bat重新双端生成（generation-scoped.log），使用其完整原生生成代码/bytes提交；没有手改生成产物，也保留已经提交的角色商店等功能，没有改写共享工作区的其他任务。独立索引只纳入本地图的Schema/运行入口差异。对最终待提交树的全部Game运行代码按Unity原有编译参数进行C#编译，退出0（scoped-compile.log，仅既有警告）；不是Unity打包或Player Build。源代码差异检查通过，原生生成代码、Unity Mesh YAML及原始日志保留制作工具的格式。

## 真实源收集与运行边界

新Prefab主地址 `Prefabs/SceneObjects/SurvivorFarmVisual` 标签arcade，单独源bundle名 `assets_game_prefabs_sceneobjects_survivorfarmvisual.bundle`；新表 `tbsurvivormaplayout.bytes` 标签startup，由原Config目录收集。常规真实BeginCollect核对地址及标签，另用BeginCollect("DefaultPackage",false,false)仅查询源依赖：环境33项依赖完整包含本次16网格和16材质，见source-dependencies.txt。没有调用构建、模拟构建或发布入口。

沿既有启动startup和入场arcade下载链，环境由EarlyStageLoad拥有句柄并准备，成功/失败/过期/结束均复用原事务。上述是源收集和源引用/控制器事务验证；未更新已发布包、未验证设备或旧安装包真实加载，未Player Build、未自然整局试玩及帧耗基准。

地形本次确认范围交付完成；本局属性界面仍独立待预览采用，不由本次地形确认扩大授权。
